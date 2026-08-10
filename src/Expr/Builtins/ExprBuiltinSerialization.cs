using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Expr.Runtime;

namespace Expr.Builtins;

internal static class ExprBuiltinSerialization
{
    private static readonly ConcurrentDictionary<int, JsonSerializerOptions> SerializerOptions = new();

    public static ExprInvocationResult ToJson(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        int maximumDepth = Math.Min(options.MaximumDepth, 256);
        long nodes = 0;
        object? normalized = Normalize(
            arguments[0],
            0,
            maximumDepth,
            new HashSet<object>(ReferenceEqualityComparer.Instance),
            options,
            ref nodes);
        JsonSerializerOptions serializerOptions = SerializerOptions.GetOrAdd(
            maximumDepth,
            static depth => new JsonSerializerOptions { MaxDepth = depth, WriteIndented = true });
        string json;
        try
        {
            json = JsonSerializer.Serialize(normalized, serializerOptions);
        }
        catch (JsonException exception)
        {
            throw new ExprRuntimeException(exception.Message, exception);
        }

        int byteCost = Encoding.UTF8.GetByteCount(json);
        ExprBuiltinCollections.EnsureAllocation(byteCost, options);
        return new ExprInvocationResult(json, (ulong)byteCost);
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
            long nodes = 0;
            object? value = ConvertElement(document.RootElement, 0, options, ref nodes);
            ExprBuiltinCollections.EnsureAllocation(nodes, options);
            return new ExprInvocationResult(value, checked((ulong)Math.Max(byteCost, nodes)));
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
        ExprBuiltinOptions options,
        ref long nodes)
    {
        if (depth > maximumDepth)
        {
            throw new ExprRuntimeException("recursion depth exceeded");
        }

        nodes++;
        ExprBuiltinCollections.EnsureAllocation(nodes, options);

        if (value is null or string or bool || ExprBuiltinValues.IsNumeric(value) ||
            value is DateTime or DateTimeOffset or TimeSpan)
        {
            return value;
        }

        if (!value.GetType().IsValueType && !active.Add(value))
        {
            throw new ExprRuntimeException("json: unsupported value: encountered a cycle");
        }

        try
        {
            if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
            {
                ExprBuiltinCollections.EnsureAllocation(array.Count, options);
                var result = new object?[array.Count];
                for (int index = 0; index < array.Count; index++)
                {
                    result[index] = Normalize(
                        array[index], depth + 1, maximumDepth, active, options, ref nodes);
                }

                return result;
            }

            if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
            {
                ExprBuiltinCollections.EnsureAllocation(map.Count, options);
                var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                foreach ((object? key, object? item) in map)
                {
                    if (key is not string text)
                    {
                        throw new ExprRuntimeException(
                            $"json: unsupported map key type: {ExprBuiltinValues.TypeNameOf(key)}");
                    }

                    result[text] = Normalize(item, depth + 1, maximumDepth, active, options, ref nodes);
                }

                return result;
            }

            return value;
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
        ref long nodes)
    {
        if (depth > options.MaximumDepth)
        {
            throw new ExprRuntimeException("recursion depth exceeded");
        }

        nodes++;
        ExprBuiltinCollections.EnsureAllocation(nodes, options);
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
                return element.GetString();
            case JsonValueKind.Number:
                double number = element.GetDouble();
                if (!double.IsFinite(number))
                {
                    throw new FormatException(
                        $"cannot unmarshal number {element.GetRawText()} into Go value of type float64");
                }

                return number;
            case JsonValueKind.Array:
                var items = new List<object?>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(ConvertElement(item, depth + 1, options, ref nodes));
                }

                return new ExprArray(items);
            case JsonValueKind.Object:
                var entries = new List<KeyValuePair<object?, object?>>();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    var entry = new KeyValuePair<object?, object?>(
                        property.Name,
                        ConvertElement(property.Value, depth + 1, options, ref nodes));
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
}
