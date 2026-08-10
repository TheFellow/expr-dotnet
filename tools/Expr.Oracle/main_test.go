package main

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"
)

func TestProcessNormalizesValuesAndKeepsGoingAfterExpressionErrors(t *testing.T) {
	input := strings.Join([]string{
		`{"id":"integer","expression":"answer + 1","environment":{"answer":41}}`,
		`{"id":"map","expression":"{2: 'two', 1: 'one'}"}`,
		`{"id":"compile-error","expression":"unknown","environment":{}}`,
		`{"id":"runtime-error","expression":"1 % zero","environment":{"zero":0}}`,
	}, "\n")
	var output bytes.Buffer

	if err := process(strings.NewReader(input), &output); err != nil {
		t.Fatal(err)
	}

	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 4 {
		t.Fatalf("got %d output lines, want 4", len(lines))
	}
	var results []response
	for _, line := range lines {
		var result response
		if err := json.Unmarshal([]byte(line), &result); err != nil {
			t.Fatal(err)
		}
		results = append(results, result)
	}

	if results[0].Status != "success" || results[0].Value == nil || results[0].Value.Kind != "integer" || results[0].Value.Value != "42" {
		t.Fatalf("unexpected integer result: %#v", results[0])
	}
	if results[1].Status != "success" || results[1].Value == nil || results[1].Value.Kind != "map" {
		t.Fatalf("unexpected map result: %#v", results[1])
	}
	if results[2].Phase != "compile" || results[2].Diagnostic == nil || results[2].Diagnostic.Line == nil {
		t.Fatalf("unexpected compile diagnostic: %#v", results[2])
	}
	if results[3].Phase != "runtime" || results[3].Diagnostic == nil || results[3].Diagnostic.Line == nil {
		t.Fatalf("unexpected runtime diagnostic: %#v", results[3])
	}
}

func TestEnvironmentNumbersHavePortableTypes(t *testing.T) {
	environment, present, err := decodeEnvironment(json.RawMessage(`{"integer":9223372036854775807,"float":1.5}`))
	if err != nil {
		t.Fatal(err)
	}
	if !present {
		t.Fatal("environment should be present")
	}
	values := environment.(map[string]any)
	if _, ok := values["integer"].(int64); !ok {
		t.Fatalf("integer has type %T, want int64", values["integer"])
	}
	if _, ok := values["float"].(float64); !ok {
		t.Fatalf("float has type %T, want float64", values["float"])
	}
}

func TestInvalidRequestBecomesAResponse(t *testing.T) {
	result := evaluateSafely(request{ID: "bad-option", Expression: "true", Options: options{ExpectedType: "decimal"}})
	if result.Status != "error" || result.Phase != "request" || result.Diagnostic == nil {
		t.Fatalf("unexpected result: %#v", result)
	}
}

func TestProcessRejectsUnknownFields(t *testing.T) {
	var output bytes.Buffer
	err := process(strings.NewReader(`{"id":"typo","expression":"true","expresson":"false"}`), &output)
	if err == nil || !strings.Contains(err.Error(), "unknown field") {
		t.Fatalf("got %v, want unknown-field error", err)
	}
}
