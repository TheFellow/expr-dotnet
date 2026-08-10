using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Optimization;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Tests.Conformance;

internal static class DotNetConformanceRunner
{
    internal static JsonObject Execute(JsonElement testCase, bool? optimizeOverride = null)
    {
        string expression = testCase.GetProperty("expression").GetString() ?? string.Empty;
        object? environment;
        ExprConfiguration configuration;
        try
        {
            (environment, configuration) = BuildRequest(testCase, optimizeOverride);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeZoneNotFoundException)
        {
            return Error("request", null, new JsonObject { ["message"] = exception.Message });
        }

        CompiledExpression compiled;
        try
        {
            compiled = ExprEngine.Compile(expression, configuration);
        }
        catch (SyntaxException exception)
        {
            return Error("compile", null, Diagnostic(exception.Diagnostic));
        }
        catch (ExprCheckException exception)
        {
            return Error("compile", null, Diagnostic(exception.Diagnostic));
        }
        catch (ExprOptimizationException exception)
        {
            return Error("compile", null, Diagnostic(exception.Message, exception.Location, expression));
        }
        catch (ExprCompilationException exception)
        {
            return Error("compile", null, Diagnostic(exception.Message, exception.Location, expression));
        }

        string semanticType = NormalizeType(compiled.SemanticModel.ResultType);
        string operation = testCase.TryGetProperty("operation", out JsonElement operationElement)
            ? operationElement.GetString() ?? "evaluate"
            : "evaluate";
        if (string.Equals(operation, "compile", StringComparison.Ordinal))
        {
            return Success("compile", semanticType, null);
        }

        object? result;
        try
        {
            result = compiled.Run(environment);
        }
        catch (ExprExecutionException exception)
        {
            return Error(
                "runtime",
                semanticType,
                Diagnostic(RuntimeMessage(exception, expression), exception.Location, expression));
        }

        try
        {
            JsonObject normalized = NormalizeValue(result);
            string runtimeType = normalized["kind"]?.GetValue<string>() ?? "any";
            return Success("runtime", runtimeType, normalized);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Error("normalize", semanticType, new JsonObject { ["message"] = exception.Message });
        }
    }

    private static (object? Environment, ExprConfiguration Configuration) BuildRequest(
        JsonElement testCase,
        bool? optimizeOverride)
    {
        object? environment = null;
        ExprConfiguration configuration = ExprConfiguration.Default;
        if (testCase.TryGetProperty("environment", out JsonElement environmentElement))
        {
            environment = ConvertJson(environmentElement);
            if (environment is IReadOnlyDictionary<string, object?> dictionary)
            {
                configuration = configuration.WithEnvironment(ExprEnvironmentSchema.FromDictionary(dictionary));
            }
        }

        JsonElement options = testCase.TryGetProperty("options", out JsonElement optionElement)
            ? optionElement
            : default;
        if (OptionBoolean(options, "allowUndefinedVariables"))
        {
            configuration = configuration.AllowUndefinedVariables();
        }

        if (optimizeOverride is bool forcedOptimization)
        {
            configuration = configuration.WithOptimization(forcedOptimization);
        }
        else if (TryOptionBoolean(options, "optimize", out bool optimize))
        {
            configuration = configuration.WithOptimization(optimize);
        }

        if (OptionBoolean(options, "disableShortCircuit"))
        {
            configuration = configuration.WithShortCircuit(false);
        }

        if (OptionBoolean(options, "disableIfOperator"))
        {
            configuration = configuration.WithIfOperatorDisabled();
        }

        if (OptionBoolean(options, "disableAllBuiltins"))
        {
            configuration = configuration.DisableAllBuiltins();
        }

        foreach (string name in OptionStrings(options, "disableBuiltins"))
        {
            configuration = configuration.DisableBuiltin(name);
        }

        foreach (string name in OptionStrings(options, "enableBuiltins"))
        {
            configuration = configuration.EnableBuiltin(name);
        }

        if (TryOptionString(options, "timezone", out string? timeZone))
        {
            configuration = configuration.WithTimeZone(timeZone!);
        }

        if (TryOptionInt32(options, "maxNodes", out int maximumNodes))
        {
            configuration = configuration.WithMaximumNodeCount(maximumNodes);
        }

        if (TryOptionString(options, "expectedType", out string? expectedType))
        {
            configuration = expectedType switch
            {
                "any" => configuration,
                "bool" => configuration.WithExpectedType(ExprTypes.Boolean),
                "int" or "int64" => configuration.WithExpectedType(ExprTypes.Integer),
                "float64" => configuration.WithExpectedType(ExprTypes.Float),
                _ => throw new ArgumentException($"unsupported expectedType {expectedType}"),
            };
        }

        return (environment, configuration);
    }

    private static object? ConvertJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out long integer) => integer,
        JsonValueKind.Number => ConvertFiniteDouble(value),
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertJson).ToList(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => ConvertJson(property.Value),
            StringComparer.Ordinal),
        _ => throw new ArgumentException($"unsupported JSON environment value {value.ValueKind}"),
    };

    private static double ConvertFiniteDouble(JsonElement value)
    {
        double result = value.GetDouble();
        if (!double.IsFinite(result))
        {
            throw new ArgumentException($"float {value.GetRawText()} is not finite binary64");
        }

        return result;
    }

    private static JsonObject NormalizeValue(object? value)
    {
        if (value is null)
        {
            return new JsonObject { ["kind"] = "null" };
        }

        if (value is DateTimeOffset instant)
        {
            return Scalar("time", FormatTime(instant));
        }

        if (value is DateTime dateTime)
        {
            return Scalar("time", FormatTime(new DateTimeOffset(dateTime)));
        }

        if (value is TimeSpan duration)
        {
            return Scalar("duration", checked(duration.Ticks * 100L).ToString(CultureInfo.InvariantCulture));
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            return Scalar("bytes", Convert.ToBase64String(memory.Span));
        }

        if (value is byte[] bytes)
        {
            return Scalar("bytes", Convert.ToBase64String(bytes));
        }

        if (value is bool boolean)
        {
            return new JsonObject { ["kind"] = "boolean", ["value"] = boolean };
        }

        if (TryFormatInteger(value, out string? integer))
        {
            return Scalar("integer", integer!);
        }

        if (value is double doubleValue)
        {
            return Scalar("float", FormatFloat(doubleValue));
        }

        if (value is float floatValue)
        {
            return Scalar("float", FormatFloat(floatValue));
        }

        if (value is string text)
        {
            return Scalar("string", text);
        }

        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            var items = new JsonArray();
            foreach (object? item in array)
            {
                items.Add(NormalizeValue(item));
            }

            return new JsonObject { ["kind"] = "array", ["value"] = items };
        }

        if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
        {
            var entries = new List<(string Key, JsonObject Entry)>();
            foreach ((object? key, object? item) in map)
            {
                JsonObject normalizedKey = NormalizeValue(key);
                var entry = new JsonObject
                {
                    ["key"] = normalizedKey,
                    ["value"] = NormalizeValue(item),
                };
                entries.Add((normalizedKey.ToJsonString(), entry));
            }

            var values = new JsonArray();
            foreach ((string _, JsonObject entry) in entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
            {
                values.Add(entry);
            }

            return new JsonObject { ["kind"] = "map", ["value"] = values };
        }

        throw new NotSupportedException($"unsupported result type {value.GetType().FullName}");
    }

    private static bool TryFormatInteger(object value, out string? result)
    {
        result = value switch
        {
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            byte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            nint number => number.ToString(CultureInfo.InvariantCulture),
            nuint number => number.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
        return result is not null;
    }

    private static string FormatFloat(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        string roundTrip = value.ToString("R", CultureInfo.InvariantCulture);
        if (value == 0D)
        {
            return roundTrip;
        }

        int exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));
        return exponent is < -4 or >= 6
            ? ToScientific(roundTrip)
            : ToDecimal(roundTrip);
    }

    private static string ToScientific(string roundTrip)
    {
        ParseDecimal(roundTrip, out bool negative, out string digits, out int decimalPosition);
        int first = 0;
        while (first < digits.Length && digits[first] == '0')
        {
            first++;
            decimalPosition--;
        }

        if (first == digits.Length)
        {
            return negative ? "-0" : "0";
        }

        digits = digits[first..].TrimEnd('0');
        int exponent = decimalPosition - 1;
        string mantissa = digits.Length == 1 ? digits : string.Concat(digits.AsSpan(0, 1), ".", digits.AsSpan(1));
        string exponentSign = exponent < 0 ? "-" : "+";
        string exponentDigits = Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        return string.Concat(negative ? "-" : string.Empty, mantissa, "e", exponentSign, exponentDigits);
    }

    private static string ToDecimal(string roundTrip)
    {
        if (!roundTrip.Contains('E', StringComparison.OrdinalIgnoreCase))
        {
            return roundTrip;
        }

        ParseDecimal(roundTrip, out bool negative, out string digits, out int decimalPosition);
        var builder = new StringBuilder();
        if (negative)
        {
            _ = builder.Append('-');
        }

        if (decimalPosition <= 0)
        {
            _ = builder.Append("0.");
            _ = builder.Append('0', -decimalPosition);
            _ = builder.Append(digits);
        }
        else if (decimalPosition >= digits.Length)
        {
            _ = builder.Append(digits);
            _ = builder.Append('0', decimalPosition - digits.Length);
        }
        else
        {
            _ = builder.Append(digits.AsSpan(0, decimalPosition));
            _ = builder.Append('.');
            _ = builder.Append(digits.AsSpan(decimalPosition));
        }

        return builder.ToString();
    }

    private static void ParseDecimal(
        string text,
        out bool negative,
        out string digits,
        out int decimalPosition)
    {
        negative = text.StartsWith('-');
        ReadOnlySpan<char> unsigned = negative ? text.AsSpan(1) : text.AsSpan();
        int exponentSeparator = unsigned.IndexOfAny('e', 'E');
        ReadOnlySpan<char> mantissa = exponentSeparator < 0 ? unsigned : unsigned[..exponentSeparator];
        int explicitExponent = exponentSeparator < 0
            ? 0
            : int.Parse(unsigned[(exponentSeparator + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        int point = mantissa.IndexOf('.');
        decimalPosition = (point < 0 ? mantissa.Length : point) + explicitExponent;
        digits = point < 0
            ? mantissa.ToString()
            : string.Concat(mantissa[..point], mantissa[(point + 1)..]);
    }

    private static string FormatTime(DateTimeOffset value)
    {
        string timestamp = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        long fractionalTicks = value.Ticks % TimeSpan.TicksPerSecond;
        if (fractionalTicks != 0)
        {
            timestamp = string.Concat(
                timestamp,
                ".",
                fractionalTicks.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0'));
        }

        return value.Offset == TimeSpan.Zero
            ? timestamp + "Z"
            : timestamp + value.ToString("zzz", CultureInfo.InvariantCulture);
    }

    private static JsonObject Scalar(string kind, string value) => new()
    {
        ["kind"] = kind,
        ["value"] = value,
    };

    private static string NormalizeType(ExprTypeDescriptor type) => type.Kind switch
    {
        ExprTypeKind.Nil => "null",
        ExprTypeKind.Boolean => "boolean",
        ExprTypeKind.Integer => "integer",
        ExprTypeKind.Float => "float",
        ExprTypeKind.String => "string",
        ExprTypeKind.Array => "array",
        ExprTypeKind.Map => "map",
        ExprTypeKind.Time => "time",
        ExprTypeKind.Duration => "duration",
        _ => "any",
    };

    private static JsonObject Success(string phase, string type, JsonObject? value)
    {
        var result = new JsonObject
        {
            ["status"] = "success",
            ["phase"] = phase,
            ["type"] = type,
        };
        if (value is not null)
        {
            result["value"] = value;
        }

        return result;
    }

    private static JsonObject Error(string phase, string? type, JsonObject diagnostic)
    {
        var result = new JsonObject
        {
            ["status"] = "error",
            ["phase"] = phase,
        };
        if (type is not null)
        {
            result["type"] = type;
        }

        result["diagnostic"] = diagnostic;
        return result;
    }

    private static JsonObject Diagnostic(SyntaxDiagnostic diagnostic) => new()
    {
        ["message"] = diagnostic.Message,
        ["from"] = diagnostic.Location.Start,
        ["to"] = diagnostic.Location.End,
        ["line"] = diagnostic.Line,
        ["column"] = diagnostic.Column + 1,
    };

    private static JsonObject Diagnostic(ExprCheckDiagnostic diagnostic)
    {
        var result = new JsonObject { ["message"] = diagnostic.Message };
        if (diagnostic.Line > 0)
        {
            AddLocation(result, diagnostic.Location, diagnostic.Line, diagnostic.Column + 1);
        }

        return result;
    }

    private static JsonObject Diagnostic(string? message, SourceLocation location, string source)
    {
        var result = new JsonObject { ["message"] = message ?? string.Empty };
        if (location != default)
        {
            (int line, int column) = Bind(source, location.Start);
            AddLocation(result, location, line, column);
        }

        return result;
    }

    private static void AddLocation(JsonObject diagnostic, SourceLocation location, int line, int column)
    {
        diagnostic["from"] = location.Start;
        diagnostic["to"] = location.End;
        diagnostic["line"] = line;
        diagnostic["column"] = column;
    }

    private static (int Line, int Column) Bind(string source, int scalarOffset)
    {
        var line = 1;
        var column = 1;
        var offset = 0;
        foreach (Rune rune in source.EnumerateRunes())
        {
            if (offset == scalarOffset)
            {
                break;
            }

            if (rune.Value == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }

            offset++;
        }

        return (line, column);
    }

    private static string RuntimeMessage(ExprExecutionException exception, string source)
    {
        (int line, int column) = Bind(source, exception.Location.Start);
        string suffix = string.Create(CultureInfo.InvariantCulture, $" ({line}:{column})");
        string firstLine = exception.Message.Split('\n', 2)[0];
        return firstLine.EndsWith(suffix, StringComparison.Ordinal)
            ? firstLine[..^suffix.Length]
            : firstLine;
    }

    private static bool OptionBoolean(JsonElement options, string name) =>
        TryOptionBoolean(options, name, out bool value) && value;

    private static bool TryOptionBoolean(JsonElement options, string name, out bool value)
    {
        if (options.ValueKind is JsonValueKind.Object && options.TryGetProperty(name, out JsonElement element))
        {
            value = element.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryOptionString(JsonElement options, string name, out string? value)
    {
        if (options.ValueKind is JsonValueKind.Object && options.TryGetProperty(name, out JsonElement element))
        {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryOptionInt32(JsonElement options, string name, out int value)
    {
        if (options.ValueKind is JsonValueKind.Object && options.TryGetProperty(name, out JsonElement element))
        {
            value = element.GetInt32();
            return true;
        }

        value = 0;
        return false;
    }

    private static IEnumerable<string> OptionStrings(JsonElement options, string name)
    {
        if (options.ValueKind is not JsonValueKind.Object || !options.TryGetProperty(name, out JsonElement element))
        {
            return [];
        }

        return element.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray();
    }
}
