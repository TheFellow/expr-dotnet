using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Expr.Runtime;

namespace Expr.Builtins;

internal static class ExprBuiltinSerialization
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<JsonMember>> JsonMembers = new();

    public static ExprInvocationResult ToJson(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        int maximumDepth = Math.Min(options.MaximumDepth, 256);
        var budget = new AllocationBudget(options.MaximumAllocation);
        object? normalized = Normalize(
            arguments[0],
            0,
            maximumDepth,
            new HashSet<object>(ReferenceEqualityComparer.Instance),
            budget);
        string json = WriteJson(normalized, maximumDepth, new AllocationBudget(options.MaximumAllocation));
        return new ExprInvocationResult(json, (ulong)Encoding.UTF8.GetByteCount(json));
    }

    public static ExprInvocationResult FromJson(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        string json = ExprBuiltinStrings.RequireString(arguments[0], "fromJSON");
        int byteCost = Encoding.UTF8.GetByteCount(json);
        ExprBuiltinCollections.EnsureAllocation(byteCost, options);
        var documentOptions = new JsonDocumentOptions
        {
            MaxDepth = Math.Min(options.MaximumDepth, 256),
        };
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, documentOptions);
            var budget = new AllocationBudget(options.MaximumAllocation);
            object? value = ConvertElement(document.RootElement, 0, options, budget);
            return new ExprInvocationResult(value, checked((ulong)Math.Max(byteCost, budget.Used)));
        }
        catch (JsonException exception)
        {
            throw new ExprRuntimeException(exception.Message, exception);
        }
        catch (FormatException exception)
        {
            throw new ExprRuntimeException(exception.Message, exception);
        }
    }

    public static ExprInvocationResult ToBase64(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        string text = ExprBuiltinStrings.RequireString(arguments[0], "toBase64");
        int byteCount = Encoding.UTF8.GetByteCount(text);
        ExprBuiltinCollections.EnsureAllocation(byteCount, options);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        long outputSize = ((long)bytes.Length + 2) / 3 * 4;
        ExprBuiltinCollections.EnsureAllocation(outputSize, options);
        string encoded = Convert.ToBase64String(bytes);
        return new ExprInvocationResult(encoded, (ulong)outputSize);
    }

    public static ExprInvocationResult FromBase64(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        string encoded = ExprBuiltinStrings.RequireString(arguments[0], "fromBase64");
        ExprBuiltinCollections.EnsureAllocation(encoded.Length, options);
        try
        {
            byte[] bytes = Convert.FromBase64String(encoded);
            ExprBuiltinCollections.EnsureAllocation(bytes.Length, options);
            return new ExprInvocationResult(Encoding.UTF8.GetString(bytes), (ulong)bytes.Length);
        }
        catch (FormatException exception)
        {
            throw new ExprRuntimeException(exception.Message, exception);
        }
    }

    private static object? Normalize(
        object? value,
        int depth,
        int maximumDepth,
        HashSet<object> active,
        AllocationBudget budget)
    {
        if (depth > maximumDepth)
        {
            throw new ExprRuntimeException("recursion depth exceeded");
        }

        switch (value)
        {
            case null or bool:
                return value;
            case string text:
                return text;
            case byte[] bytes:
                return Convert.ToBase64String(bytes);
            case ReadOnlyMemory<byte> bytes:
                return Convert.ToBase64String(bytes.Span);
            case TimeSpan duration:
                try
                {
                    return checked(duration.Ticks * 100L);
                }
                catch (OverflowException exception)
                {
                    throw new ExprRuntimeException("json: unsupported time.Duration outside int64 nanoseconds", exception);
                }
            case DateTimeOffset time:
                return FormatTime(time);
            case DateTime time:
                return FormatTime(ToDateTimeOffset(time));
            default:
                if (ExprBuiltinValues.IsNumeric(value))
                {
                    if (value is double doubleValue && !double.IsFinite(doubleValue) ||
                        value is float floatValue && !float.IsFinite(floatValue) ||
                        value is Half halfValue && !Half.IsFinite(halfValue))
                    {
                        throw new ExprRuntimeException($"json: unsupported value: {value}");
                    }

                    return value;
                }

                break;
        }

        if (!value.GetType().IsValueType && !active.Add(value))
        {
            throw new ExprRuntimeException("json: unsupported value: encountered a cycle");
        }

        try
        {
            if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
            {
                budget.Charge(array.Count);
                var result = new object?[array.Count];
                for (int index = 0; index < array.Count; index++)
                {
                    result[index] = Normalize(array[index], depth + 1, maximumDepth, active, budget);
                }

                return result;
            }

            if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
            {
                budget.Charge(map.Count);
                var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                foreach ((object? key, object? item) in map)
                {
                    if (key is not string text)
                    {
                        throw new ExprRuntimeException(
                            $"json: unsupported map key type: {ExprBuiltinValues.TypeNameOf(key)}");
                    }

                    result[text] = Normalize(item, depth + 1, maximumDepth, active, budget);
                }

                return result;
            }

            IReadOnlyList<JsonMember> members = JsonMembers.GetOrAdd(value.GetType(), BuildJsonMembers);
            budget.Charge(members.Count);
            var objectResult = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (JsonMember member in members)
            {
                objectResult[member.Name] = Normalize(
                    member.GetValue(value), depth + 1, maximumDepth, active, budget);
            }

            return objectResult;
        }
        finally
        {
            if (!value.GetType().IsValueType)
            {
                active.Remove(value);
            }
        }
    }

    private static object? ConvertElement(
        JsonElement element,
        int depth,
        ExprBuiltinOptions options,
        AllocationBudget budget)
    {
        if (depth > options.MaximumDepth)
        {
            throw new ExprRuntimeException("recursion depth exceeded");
        }

        budget.Charge(1);
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                string? text = element.GetString();
                budget.Charge(text is null ? 0 : Encoding.UTF8.GetByteCount(text));
                return text;
            case JsonValueKind.Number:
                double number = element.GetDouble();
                if (!double.IsFinite(number))
                {
                    throw new FormatException(
                        $"cannot unmarshal number {element.GetRawText()} into Go value of type float64");
                }

                return number;
            case JsonValueKind.Array:
                int arrayLength = element.GetArrayLength();
                budget.Charge(arrayLength);
                var items = new object?[arrayLength];
                int itemIndex = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items[itemIndex++] = ConvertElement(item, depth + 1, options, budget);
                }

                return new ExprArray(items);
            case JsonValueKind.Object:
                var entries = new List<KeyValuePair<object?, object?>>();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    budget.Charge(1L + Encoding.UTF8.GetByteCount(property.Name));
                    var entry = new KeyValuePair<object?, object?>(
                        property.Name,
                        ConvertElement(property.Value, depth + 1, options, budget));
                    int existing = entries.FindIndex(pair =>
                        string.Equals((string?)pair.Key, property.Name, StringComparison.Ordinal));
                    if (existing >= 0)
                    {
                        entries[existing] = entry;
                    }
                    else
                    {
                        entries.Add(entry);
                    }
                }

                return new ExprMap(entries);
            default:
                throw new ExprRuntimeException(
                    string.Format(CultureInfo.InvariantCulture, "unsupported JSON token {0}", element.ValueKind));
        }
    }

    private static string WriteJson(object? value, int maximumDepth, AllocationBudget budget)
    {
        var output = new StringBuilder();
        var work = new Stack<JsonFrame>();
        work.Push(JsonFrame.ForValue(value, 0));
        while (work.TryPop(out JsonFrame frame))
        {
            if (frame.IsText)
            {
                Append(output, frame.Text!, budget);
                continue;
            }

            if (frame.Depth > maximumDepth)
            {
                throw new ExprRuntimeException("recursion depth exceeded");
            }

            if (frame.Value is object?[] array)
            {
                PushArray(array, frame.Depth, work);
            }
            else if (frame.Value is SortedDictionary<string, object?> map)
            {
                PushObject(map, frame.Depth, work);
            }
            else
            {
                AppendScalar(output, frame.Value, budget);
            }
        }

        return output.ToString();
    }

    private static void PushArray(object?[] array, int depth, Stack<JsonFrame> work)
    {
        if (array.Length == 0)
        {
            work.Push(JsonFrame.TextValue("[]"));
            return;
        }

        work.Push(JsonFrame.TextValue(string.Concat("\n", Indent(depth), "]")));
        for (int index = array.Length - 1; index >= 0; index--)
        {
            if (index < array.Length - 1)
            {
                work.Push(JsonFrame.TextValue(",\n"));
            }

            work.Push(JsonFrame.ForValue(array[index], depth + 1));
            work.Push(JsonFrame.TextValue(Indent(depth + 1)));
        }

        work.Push(JsonFrame.TextValue("[\n"));
    }

    private static void PushObject(SortedDictionary<string, object?> map, int depth, Stack<JsonFrame> work)
    {
        if (map.Count == 0)
        {
            work.Push(JsonFrame.TextValue("{}"));
            return;
        }

        work.Push(JsonFrame.TextValue(string.Concat("\n", Indent(depth), "}")));
        KeyValuePair<string, object?>[] entries = map.ToArray();
        for (int index = entries.Length - 1; index >= 0; index--)
        {
            if (index < entries.Length - 1)
            {
                work.Push(JsonFrame.TextValue(",\n"));
            }

            work.Push(JsonFrame.ForValue(entries[index].Value, depth + 1));
            work.Push(JsonFrame.TextValue(": "));
            work.Push(JsonFrame.Quoted(entries[index].Key));
            work.Push(JsonFrame.TextValue(Indent(depth + 1)));
        }

        work.Push(JsonFrame.TextValue("{\n"));
    }

    private static void AppendScalar(StringBuilder output, object? value, AllocationBudget budget)
    {
        switch (value)
        {
            case null:
                Append(output, "null", budget);
                return;
            case string text:
                AppendQuoted(output, text, budget);
                return;
            case bool boolean:
                Append(output, boolean ? "true" : "false", budget);
                return;
            case sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint:
                Append(output, Convert.ToString(value, CultureInfo.InvariantCulture)!, budget);
                return;
            case Half half:
                Append(output, JsonSerializer.Serialize((float)half), budget);
                return;
            case float number:
                Append(output, JsonSerializer.Serialize(number), budget);
                return;
            case double number:
                Append(output, JsonSerializer.Serialize(number), budget);
                return;
            default:
                throw new ExprRuntimeException(
                    $"json: unsupported value: {ExprBuiltinValues.TypeNameOf(value)}");
        }
    }

    private static void AppendQuoted(StringBuilder output, string value, AllocationBudget budget)
    {
        Append(output, "\"", budget);
        int plainStart = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            string? escaped = character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '<' => "\\u003c",
                '>' => "\\u003e",
                '&' => "\\u0026",
                '\u2028' => "\\u2028",
                '\u2029' => "\\u2029",
                _ when character < ' ' => string.Create(
                    6,
                    character,
                    static (span, item) =>
                    {
                        "\\u00".AsSpan().CopyTo(span);
                        const string hex = "0123456789abcdef";
                        span[4] = hex[item >> 4];
                        span[5] = hex[item & 15];
                    }),
                _ => null,
            };
            if (escaped is null)
            {
                continue;
            }

            if (index > plainStart)
            {
                Append(output, value[plainStart..index], budget);
            }

            Append(output, escaped, budget);
            plainStart = index + 1;
        }

        if (plainStart < value.Length)
        {
            Append(output, value[plainStart..], budget);
        }

        Append(output, "\"", budget);
    }

    private static void Append(StringBuilder output, string value, AllocationBudget budget)
    {
        budget.Charge(Encoding.UTF8.GetByteCount(value));
        _ = output.Append(value);
    }

    private static string Indent(int depth) => new(' ', checked(depth * 2));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => new DateTimeOffset(value),
        DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero),
    };

    private static string FormatTime(DateTimeOffset value)
    {
        string result = value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
        return value.Offset == TimeSpan.Zero ? string.Concat(result.AsSpan(0, result.Length - 6), "Z") : result;
    }

    private static IReadOnlyList<JsonMember> BuildJsonMembers(Type type)
    {
        IEnumerable<JsonMember> properties = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .Where(static property => property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition is not JsonIgnoreCondition.Always)
            .Select(static property => new JsonMember(
                property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name,
                property.GetValue));
        IEnumerable<JsonMember> fields = type
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(static field => field.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition is not JsonIgnoreCondition.Always)
            .Select(static field => new JsonMember(
                field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? field.Name,
                field.GetValue));
        return Array.AsReadOnly(properties
            .Concat(fields)
            .GroupBy(static member => member.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static member => member.Name, StringComparer.Ordinal)
            .ToArray());
    }

    private sealed record JsonMember(string Name, Func<object, object?> GetValue);

    private sealed class AllocationBudget(long maximum)
    {
        public long Used { get; private set; }

        public void Charge(long amount)
        {
            try
            {
                Used = checked(Used + amount);
            }
            catch (OverflowException exception)
            {
                throw new ExprRuntimeException("memory budget exceeded", exception);
            }

            if (amount < 0 || Used > maximum || Used > int.MaxValue)
            {
                throw new ExprRuntimeException("memory budget exceeded");
            }
        }
    }

    private readonly record struct JsonFrame(object? Value, string? Text, int Depth, bool IsText)
    {
        public static JsonFrame ForValue(object? value, int depth) => new(value, null, depth, false);

        public static JsonFrame TextValue(string text) => new(null, text, 0, true);

        public static JsonFrame Quoted(string text) => new(text, null, 0, false);
    }
}
