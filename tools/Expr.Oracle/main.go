// Expr.Oracle is a JSON Lines differential oracle for Expr.NET.
//
// It intentionally exposes only JSON-compatible environments and portable
// configuration options. Host reflection and custom-function conformance need
// dedicated fixtures in each implementation rather than an invented wire ABI.
package main

import (
	"bufio"
	"bytes"
	"encoding/base64"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"math"
	"os"
	"reflect"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/expr-lang/expr"
	"github.com/expr-lang/expr/file"
)

const (
	caseSchema       = "expr.conformance.case/v1"
	resultSchema     = "expr.conformance.result/v1"
	upstreamRevision = "4b31df3a2e0eefec04c017a82a00e0f08541d3e4"
	maximumLineBytes = 4 * 1024 * 1024
)

type request struct {
	Schema      string          `json:"schema,omitempty"`
	ID          string          `json:"id"`
	Expression  string          `json:"expression"`
	Operation   string          `json:"operation,omitempty"`
	Environment json.RawMessage `json:"environment,omitempty"`
	Options     options         `json:"options,omitempty"`
	Expected    json.RawMessage `json:"expected,omitempty"`
	Provenance  json.RawMessage `json:"provenance,omitempty"`
}

type options struct {
	AllowUndefinedVariables bool     `json:"allowUndefinedVariables,omitempty"`
	Optimize                *bool    `json:"optimize,omitempty"`
	DisableShortCircuit     bool     `json:"disableShortCircuit,omitempty"`
	DisableIfOperator       bool     `json:"disableIfOperator,omitempty"`
	DisableAllBuiltins      bool     `json:"disableAllBuiltins,omitempty"`
	DisableBuiltins         []string `json:"disableBuiltins,omitempty"`
	EnableBuiltins          []string `json:"enableBuiltins,omitempty"`
	Timezone                string   `json:"timezone,omitempty"`
	MaxNodes                *uint    `json:"maxNodes,omitempty"`
	ExpectedType            string   `json:"expectedType,omitempty"`
}

type response struct {
	Schema           string           `json:"schema"`
	ID               string           `json:"id"`
	UpstreamRevision string           `json:"upstreamRevision"`
	Status           string           `json:"status"`
	Phase            string           `json:"phase"`
	Type             string           `json:"type,omitempty"`
	Value            *normalizedValue `json:"value,omitempty"`
	Diagnostic       *diagnostic      `json:"diagnostic,omitempty"`
}

type diagnostic struct {
	Message string `json:"message"`
	From    *int   `json:"from,omitempty"`
	To      *int   `json:"to,omitempty"`
	Line    *int   `json:"line,omitempty"`
	Column  *int   `json:"column,omitempty"`
}

type normalizedValue struct {
	Kind  string `json:"kind"`
	Value any    `json:"value,omitempty"`
}

type normalizedMapEntry struct {
	Key   normalizedValue `json:"key"`
	Value normalizedValue `json:"value"`
}

func main() {
	flag.Parse()
	if flag.NArg() != 0 {
		fmt.Fprintln(os.Stderr, "usage: expr-oracle < requests.jsonl > results.jsonl")
		os.Exit(2)
	}

	if err := process(os.Stdin, os.Stdout); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func process(input io.Reader, output io.Writer) error {
	scanner := bufio.NewScanner(input)
	scanner.Buffer(make([]byte, 64*1024), maximumLineBytes)
	encoder := json.NewEncoder(output)
	encoder.SetEscapeHTML(false)

	lineNumber := 0
	for scanner.Scan() {
		lineNumber++
		line := bytes.TrimSpace(scanner.Bytes())
		if len(line) == 0 {
			continue
		}

		var req request
		decoder := json.NewDecoder(bytes.NewReader(line))
		decoder.UseNumber()
		decoder.DisallowUnknownFields()
		if err := decoder.Decode(&req); err != nil {
			return fmt.Errorf("line %d: decode request: %w", lineNumber, err)
		}
		if err := ensureEndOfJSON(decoder); err != nil {
			return fmt.Errorf("line %d: %w", lineNumber, err)
		}

		result := evaluateSafely(req)
		if err := encoder.Encode(result); err != nil {
			return fmt.Errorf("line %d: encode response: %w", lineNumber, err)
		}
	}
	if err := scanner.Err(); err != nil {
		return fmt.Errorf("read requests: %w", err)
	}
	return nil
}

func ensureEndOfJSON(decoder *json.Decoder) error {
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		if err == nil {
			return errors.New("request contains more than one JSON value")
		}
		return fmt.Errorf("decode request suffix: %w", err)
	}
	return nil
}

func evaluateSafely(req request) (result response) {
	result = response{
		Schema:           resultSchema,
		ID:               req.ID,
		UpstreamRevision: upstreamRevision,
		Status:           "error",
		Phase:            "oracle",
	}
	defer func() {
		if recovered := recover(); recovered != nil {
			result.Diagnostic = &diagnostic{Message: fmt.Sprintf("%v", recovered)}
		}
	}()
	return evaluate(req)
}

func evaluate(req request) response {
	result := response{
		Schema:           resultSchema,
		ID:               req.ID,
		UpstreamRevision: upstreamRevision,
		Status:           "error",
		Phase:            "request",
	}
	if req.ID == "" {
		result.Diagnostic = &diagnostic{Message: "id is required"}
		return result
	}
	if req.Schema != "" && req.Schema != caseSchema {
		result.Diagnostic = &diagnostic{Message: fmt.Sprintf("unsupported schema %q", req.Schema)}
		return result
	}
	operation := req.Operation
	if operation == "" {
		operation = "evaluate"
	}
	if operation != "compile" && operation != "evaluate" {
		result.Diagnostic = &diagnostic{Message: fmt.Sprintf("unsupported operation %q", operation)}
		return result
	}

	environment, hasEnvironment, err := decodeEnvironment(req.Environment)
	if err != nil {
		result.Diagnostic = &diagnostic{Message: err.Error()}
		return result
	}

	compileOptions, err := buildOptions(req.Options, environment, hasEnvironment)
	if err != nil {
		result.Diagnostic = &diagnostic{Message: err.Error()}
		return result
	}

	program, err := expr.Compile(req.Expression, compileOptions...)
	if err != nil {
		result.Phase = "compile"
		result.Diagnostic = normalizeDiagnostic(err)
		return result
	}

	result.Type = normalizeReflectType(program.Node().Type())
	if operation == "compile" {
		result.Status = "success"
		result.Phase = "compile"
		return result
	}

	value, err := expr.Run(program, environment)
	if err != nil {
		result.Phase = "runtime"
		result.Diagnostic = normalizeDiagnostic(err)
		return result
	}
	normalized, err := normalizeValue(value)
	if err != nil {
		result.Phase = "normalize"
		result.Diagnostic = &diagnostic{Message: err.Error()}
		return result
	}
	result.Status = "success"
	result.Phase = "runtime"
	result.Type = normalized.Kind
	result.Value = &normalized
	return result
}

func decodeEnvironment(raw json.RawMessage) (any, bool, error) {
	if len(raw) == 0 {
		return nil, false, nil
	}
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	var decoded any
	if err := decoder.Decode(&decoded); err != nil {
		return nil, false, fmt.Errorf("decode environment: %w", err)
	}
	converted, err := convertJSONNumbers(decoded)
	if err != nil {
		return nil, false, fmt.Errorf("decode environment: %w", err)
	}
	return converted, true, nil
}

func convertJSONNumbers(value any) (any, error) {
	switch typed := value.(type) {
	case json.Number:
		text := typed.String()
		if !strings.ContainsAny(text, ".eE") {
			integer, err := strconv.ParseInt(text, 10, 64)
			if err != nil {
				return nil, fmt.Errorf("integer %q is outside the signed 64-bit range", text)
			}
			return integer, nil
		}
		floating, err := strconv.ParseFloat(text, 64)
		if err != nil || math.IsInf(floating, 0) || math.IsNaN(floating) {
			return nil, fmt.Errorf("float %q is not finite binary64", text)
		}
		return floating, nil
	case []any:
		converted := make([]any, len(typed))
		for index, item := range typed {
			value, err := convertJSONNumbers(item)
			if err != nil {
				return nil, err
			}
			converted[index] = value
		}
		return converted, nil
	case map[string]any:
		converted := make(map[string]any, len(typed))
		for key, item := range typed {
			value, err := convertJSONNumbers(item)
			if err != nil {
				return nil, err
			}
			converted[key] = value
		}
		return converted, nil
	default:
		return value, nil
	}
}

func buildOptions(config options, environment any, hasEnvironment bool) ([]expr.Option, error) {
	result := make([]expr.Option, 0, 12)
	if hasEnvironment {
		result = append(result, expr.Env(environment))
	}
	if config.AllowUndefinedVariables {
		result = append(result, expr.AllowUndefinedVariables())
	}
	if config.Optimize != nil {
		result = append(result, expr.Optimize(*config.Optimize))
	}
	if config.DisableShortCircuit {
		result = append(result, expr.DisableShortCircuit())
	}
	if config.DisableIfOperator {
		result = append(result, expr.DisableIfOperator())
	}
	if config.DisableAllBuiltins {
		result = append(result, expr.DisableAllBuiltins())
	}
	for _, name := range config.DisableBuiltins {
		result = append(result, expr.DisableBuiltin(name))
	}
	for _, name := range config.EnableBuiltins {
		result = append(result, expr.EnableBuiltin(name))
	}
	if config.Timezone != "" {
		if _, err := time.LoadLocation(config.Timezone); err != nil {
			return nil, fmt.Errorf("invalid timezone %q: %w", config.Timezone, err)
		}
		result = append(result, expr.Timezone(config.Timezone))
	}
	if config.MaxNodes != nil {
		result = append(result, expr.MaxNodes(*config.MaxNodes))
	}
	switch config.ExpectedType {
	case "", "any":
		if config.ExpectedType == "any" {
			result = append(result, expr.AsAny())
		}
	case "bool":
		result = append(result, expr.AsBool())
	case "int":
		result = append(result, expr.AsInt())
	case "int64":
		result = append(result, expr.AsInt64())
	case "float64":
		result = append(result, expr.AsFloat64())
	default:
		return nil, fmt.Errorf("unsupported expectedType %q", config.ExpectedType)
	}
	return result, nil
}

func normalizeDiagnostic(err error) *diagnostic {
	result := &diagnostic{Message: err.Error()}
	var sourceError *file.Error
	if !errors.As(err, &sourceError) {
		return result
	}
	result.Message = sourceError.Message
	from := sourceError.From
	to := sourceError.To
	result.From = &from
	result.To = &to
	if sourceError.Line > 0 {
		line := sourceError.Line
		column := sourceError.Column + 1
		result.Line = &line
		result.Column = &column
	}
	return result
}

func normalizeValue(value any) (normalizedValue, error) {
	if value == nil {
		return normalizedValue{Kind: "null"}, nil
	}
	if instant, ok := value.(time.Time); ok {
		return normalizedValue{Kind: "time", Value: instant.Format(time.RFC3339Nano)}, nil
	}
	if duration, ok := value.(time.Duration); ok {
		return normalizedValue{Kind: "duration", Value: strconv.FormatInt(int64(duration), 10)}, nil
	}
	if data, ok := value.([]byte); ok {
		return normalizedValue{Kind: "bytes", Value: base64.StdEncoding.EncodeToString(data)}, nil
	}

	reflected := reflect.ValueOf(value)
	for reflected.Kind() == reflect.Interface || reflected.Kind() == reflect.Pointer {
		if reflected.IsNil() {
			return normalizedValue{Kind: "null"}, nil
		}
		reflected = reflected.Elem()
	}

	switch reflected.Kind() {
	case reflect.Bool:
		return normalizedValue{Kind: "boolean", Value: reflected.Bool()}, nil
	case reflect.Int, reflect.Int8, reflect.Int16, reflect.Int32, reflect.Int64:
		return normalizedValue{Kind: "integer", Value: strconv.FormatInt(reflected.Int(), 10)}, nil
	case reflect.Uint, reflect.Uint8, reflect.Uint16, reflect.Uint32, reflect.Uint64, reflect.Uintptr:
		return normalizedValue{Kind: "integer", Value: strconv.FormatUint(reflected.Uint(), 10)}, nil
	case reflect.Float32, reflect.Float64:
		bits := reflected.Type().Bits()
		return normalizedValue{Kind: "float", Value: formatFloat(reflected.Float(), bits)}, nil
	case reflect.String:
		return normalizedValue{Kind: "string", Value: reflected.String()}, nil
	case reflect.Array, reflect.Slice:
		items := make([]normalizedValue, reflected.Len())
		for index := 0; index < reflected.Len(); index++ {
			item, err := normalizeValue(reflected.Index(index).Interface())
			if err != nil {
				return normalizedValue{}, err
			}
			items[index] = item
		}
		return normalizedValue{Kind: "array", Value: items}, nil
	case reflect.Map:
		entries := make([]normalizedMapEntry, 0, reflected.Len())
		iterator := reflected.MapRange()
		for iterator.Next() {
			key, err := normalizeValue(iterator.Key().Interface())
			if err != nil {
				return normalizedValue{}, err
			}
			item, err := normalizeValue(iterator.Value().Interface())
			if err != nil {
				return normalizedValue{}, err
			}
			entries = append(entries, normalizedMapEntry{Key: key, Value: item})
		}
		sort.Slice(entries, func(left, right int) bool {
			return canonicalJSON(entries[left].Key) < canonicalJSON(entries[right].Key)
		})
		return normalizedValue{Kind: "map", Value: entries}, nil
	default:
		return normalizedValue{}, fmt.Errorf("unsupported result type %s", reflected.Type())
	}
}

func formatFloat(value float64, bits int) string {
	switch {
	case math.IsNaN(value):
		return "NaN"
	case math.IsInf(value, 1):
		return "Infinity"
	case math.IsInf(value, -1):
		return "-Infinity"
	default:
		return strconv.FormatFloat(value, 'g', -1, bits)
	}
}

func canonicalJSON(value any) string {
	encoded, err := json.Marshal(value)
	if err != nil {
		panic(err)
	}
	return string(encoded)
}

func normalizeReflectType(value reflect.Type) string {
	if value == nil {
		return "any"
	}
	if value == reflect.TypeOf(time.Time{}) {
		return "time"
	}
	if value == reflect.TypeOf(time.Duration(0)) {
		return "duration"
	}
	for value.Kind() == reflect.Pointer {
		value = value.Elem()
	}
	switch value.Kind() {
	case reflect.Bool:
		return "boolean"
	case reflect.Int, reflect.Int8, reflect.Int16, reflect.Int32, reflect.Int64,
		reflect.Uint, reflect.Uint8, reflect.Uint16, reflect.Uint32, reflect.Uint64, reflect.Uintptr:
		return "integer"
	case reflect.Float32, reflect.Float64:
		return "float"
	case reflect.String:
		return "string"
	case reflect.Array, reflect.Slice:
		if value.Elem().Kind() == reflect.Uint8 {
			return "bytes"
		}
		return "array"
	case reflect.Map, reflect.Struct:
		return "map"
	case reflect.Invalid:
		return "null"
	default:
		return "any"
	}
}
