using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Expr.Runtime;

namespace Expr.Builtins;

internal static class ExprBuiltinValues
{
    public static object? Len(ReadOnlySpan<object?> arguments)
    {
        object? value = arguments[0];
        if (value is string text)
        {
            long count = 0;
            foreach (Rune _ in text.EnumerateRunes())
            {
                count++;
            }

            return count;
        }

        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            return (long)array.Count;
        }

        if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
        {
            return (long)map.Count;
        }

        throw Error("len", value);
    }

    public static object TypeName(ReadOnlySpan<object?> arguments)
    {
        object? value = arguments[0];
        return value switch
        {
            null => "nil",
            bool => "bool",
            sbyte or short or int or long or nint => "int",
            byte or ushort or uint or ulong or nuint => "uint",
            Half or float or double => "float",
            string => "string",
            DateTime or DateTimeOffset => "time.Time",
            TimeSpan => "time.Duration",
            Delegate => "func",
            _ when ExprCollections.TryAsArray(value, out _) => "array",
            _ when ExprCollections.TryAsMap(value, out _) => "map",
            _ => value.GetType().FullName ?? "struct",
        };
    }

    public static object Abs(ReadOnlySpan<object?> arguments) => arguments[0] switch
    {
        sbyte value => unchecked((sbyte)(value < 0 ? -value : value)),
        byte value => value,
        short value => unchecked((short)(value < 0 ? -value : value)),
        ushort value => value,
        int value => unchecked(value < 0 ? -value : value),
        uint value => value,
        long value => unchecked(value < 0 ? -value : value),
        ulong value => value,
        nint value => unchecked(value < 0 ? -value : value),
        nuint value => value,
        Half value => Half.Abs(value),
        float value => MathF.Abs(value),
        double value => Math.Abs(value),
        _ => throw Error("abs", arguments[0]),
    };

    public static object Ceil(ReadOnlySpan<object?> arguments) => Math.Ceiling(ToDouble(arguments[0], "ceil"));

    public static object Floor(ReadOnlySpan<object?> arguments) => Math.Floor(ToDouble(arguments[0], "floor"));

    public static object Round(ReadOnlySpan<object?> arguments) =>
        Math.Round(ToDouble(arguments[0], "round"), MidpointRounding.AwayFromZero);

    public static object Int(ReadOnlySpan<object?> arguments)
    {
        object? value = arguments[0];
        if (value is Enum enumeration)
        {
            try
            {
                return Convert.ToInt64(enumeration, CultureInfo.InvariantCulture);
            }
            catch (OverflowException exception)
            {
                throw new ExprRuntimeException($"invalid operation: int({TypeNameOf(value)})", exception);
            }
        }

        if (value is string text)
        {
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }

            throw new ExprRuntimeException($"invalid operation: int({text})");
        }

        try
        {
            return ExprValue.ToInt64(value);
        }
        catch (ExprRuntimeException)
        {
            throw new ExprRuntimeException($"invalid operation: int({TypeNameOf(value)})");
        }
    }

    public static object Float(ReadOnlySpan<object?> arguments)
    {
        object? value = arguments[0];
        if (value is string text)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            throw new ExprRuntimeException($"invalid operation: float({text})");
        }

        try
        {
            return ExprValue.ToDouble(value);
        }
        catch (ExprRuntimeException)
        {
            throw new ExprRuntimeException($"invalid operation: float({TypeNameOf(value)})");
        }
    }

    public static object String(ReadOnlySpan<object?> arguments) => Format(arguments[0]);

    public static ExprInvocationResult StringSafe(
        ReadOnlySpan<object?> arguments,
        ExprBuiltinOptions options)
    {
        ulong cost = EstimateString(arguments);
        if (cost > (ulong)options.MaximumAllocation)
        {
            throw new ExprRuntimeException("memory budget exceeded");
        }

        return new ExprInvocationResult(Format(arguments[0]), cost);
    }

    public static ulong EstimateString(ReadOnlySpan<object?> arguments) =>
        EstimateFormat(arguments[0]);

    public static object BinaryBit(
        ReadOnlySpan<object?> arguments,
        string name,
        Func<long, long, long> operation)
    {
        long left = RequireInteger(arguments[0], name);
        long right = RequireInteger(arguments[1], name);
        return operation(left, right);
    }

    public static object Shift(ReadOnlySpan<object?> arguments, string name, bool left, bool unsigned)
    {
        long value = RequireInteger(arguments[0], name);
        long shift = RequireInteger(arguments[1], name);
        if (shift < 0)
        {
            throw new ExprRuntimeException($"invalid operation: negative shift count {shift} (type int)");
        }

        if (shift >= 64)
        {
            return left ? 0L : unsigned ? 0L : value < 0 ? -1L : 0L;
        }

        int amount = (int)shift;
        return left
            ? unchecked(value << amount)
            : unsigned
                ? unchecked((long)((ulong)value >> amount))
                : value >> amount;
    }

    public static object BitNot(ReadOnlySpan<object?> arguments) => ~RequireInteger(arguments[0], "bitnot");

    internal static bool IsNumeric(object? value) => value is
        sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or Half or float or double;

    internal static double ToDouble(object? value, string name)
    {
        try
        {
            return ExprValue.ToDouble(value);
        }
        catch (ExprRuntimeException)
        {
            throw Error(name, value);
        }
    }

    internal static long RequireInteger(object? value, string name)
    {
        if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint))
        {
            throw new ExprRuntimeException(
                $"cannot use {TypeNameOf(value)} as argument (type int) to call {name}");
        }

        return ExprValue.ToInt64(value);
    }

    internal static string TypeNameOf(object? value) => value switch
    {
        null => "nil",
        string => "string",
        bool => "bool",
        sbyte or short or int or long or nint => "int",
        byte or ushort or uint or ulong or nuint => "uint",
        Half or float or double => "float",
        _ => value.GetType().Name,
    };

    private static string Format(object? value)
    {
        const int maximumDepth = 10_000;
        const int maximumLength = 1_000_000;
        var result = new StringBuilder();
        var activeCollections = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var work = new Stack<FormatFrame>();
        work.Push(FormatFrame.Value(value, 0));

        while (work.Count > 0)
        {
            FormatFrame frame = work.Pop();
            if (frame.Kind is FormatFrameKind.Text)
            {
                AppendBounded(result, frame.Text!, maximumLength);
                continue;
            }

            if (frame.Kind is FormatFrameKind.ExitCollection)
            {
                _ = activeCollections.Remove(frame.Item!);
                continue;
            }

            object? item = frame.Item;
            if (ExprCollections.TryAsArray(item, out IExprArray? array) && array is not null)
            {
                PushArray(array, item!, frame.Depth, result, work, activeCollections, maximumDepth, maximumLength);
                continue;
            }

            if (ExprCollections.TryAsMap(item, out IExprMap? map) && map is not null)
            {
                PushMap(map, item!, frame.Depth, result, work, activeCollections, maximumDepth, maximumLength);
                continue;
            }

            AppendBounded(result, FormatScalar(item), maximumLength);
        }

        return result.ToString();
    }

    private static void PushArray(
        IExprArray array,
        object identity,
        int depth,
        StringBuilder result,
        Stack<FormatFrame> work,
        HashSet<object> activeCollections,
        int maximumDepth,
        int maximumLength)
    {
        if (depth >= maximumDepth)
        {
            throw new ExprRuntimeException($"string formatting exceeds maximum depth of {maximumDepth}");
        }

        if (!activeCollections.Add(identity))
        {
            AppendBounded(result, "<cycle>", maximumLength);
            return;
        }

        AppendBounded(result, "[", maximumLength);
        work.Push(FormatFrame.Exit(identity));
        work.Push(FormatFrame.TextValue("]"));
        for (var index = array.Count - 1; index >= 0; index--)
        {
            work.Push(FormatFrame.Value(array[index], depth + 1));
            if (index > 0)
            {
                work.Push(FormatFrame.TextValue(" "));
            }
        }
    }

    private static void PushMap(
        IExprMap map,
        object identity,
        int depth,
        StringBuilder result,
        Stack<FormatFrame> work,
        HashSet<object> activeCollections,
        int maximumDepth,
        int maximumLength)
    {
        if (depth >= maximumDepth)
        {
            throw new ExprRuntimeException($"string formatting exceeds maximum depth of {maximumDepth}");
        }

        if (!activeCollections.Add(identity))
        {
            AppendBounded(result, "<cycle>", maximumLength);
            return;
        }

        List<KeyValuePair<object?, object?>> entries = map.ToList();
        entries.Sort(static (left, right) => string.CompareOrdinal(
            StableMapKey(left.Key),
            StableMapKey(right.Key)));
        AppendBounded(result, "map[", maximumLength);
        work.Push(FormatFrame.Exit(identity));
        work.Push(FormatFrame.TextValue("]"));
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            KeyValuePair<object?, object?> entry = entries[index];
            work.Push(FormatFrame.Value(entry.Value, depth + 1));
            work.Push(FormatFrame.TextValue(":"));
            work.Push(FormatFrame.Value(entry.Key, depth + 1));
            if (index > 0)
            {
                work.Push(FormatFrame.TextValue(" "));
            }
        }
    }

    private static string StableMapKey(object? key) => string.Concat(TypeNameOf(key), "\0", FormatScalar(key));

    private static string FormatScalar(object? value) => value switch
    {
        null => "<nil>",
        _ => ExprDisplay.Value(value),
    };

    private static ulong EstimateFormat(object? value)
    {
        const int maximumDepth = 10_000;
        const ulong maximumLength = 1_000_000;
        ulong length = 0;
        var activeCollections = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var activeEnumerators = new List<IEnumerator<KeyValuePair<object?, object?>>>();
        var work = new Stack<EstimateFrame>();
        work.Push(EstimateFrame.Value(value, 0));
        try
        {
            while (work.TryPop(out EstimateFrame frame))
            {
                switch (frame.Kind)
                {
                    case EstimateFrameKind.Value:
                        EstimateValue(frame.Item, frame.Depth);
                        break;
                    case EstimateFrameKind.Array:
                        if (frame.Index < frame.Array!.Count)
                        {
                            work.Push(frame with { Index = frame.Index + 1 });
                            work.Push(EstimateFrame.Value(frame.Array[frame.Index], frame.Depth + 1));
                        }

                        break;
                    case EstimateFrameKind.Map:
                        if (frame.Map!.MoveNext())
                        {
                            work.Push(frame);
                            work.Push(EstimateFrame.Value(frame.Map.Current.Value, frame.Depth + 1));
                            work.Push(EstimateFrame.Value(frame.Map.Current.Key, frame.Depth + 1));
                        }

                        break;
                    case EstimateFrameKind.Exit:
                        _ = activeCollections.Remove(frame.Item!);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown format-estimation frame.");
                }
            }
        }
        finally
        {
            foreach (IEnumerator<KeyValuePair<object?, object?>> enumerator in activeEnumerators)
            {
                enumerator.Dispose();
            }
        }

        return length;

        void EstimateValue(object? item, int depth)
        {
            if (ExprCollections.TryAsArray(item, out IExprArray? array) && array is not null)
            {
                if (!EnterCollection(item!, depth))
                {
                    return;
                }

                Add(checked((ulong)array.Count + 1UL));
                work.Push(EstimateFrame.Exit(item!));
                work.Push(EstimateFrame.ArrayValue(array, depth));
                return;
            }

            if (ExprCollections.TryAsMap(item, out IExprMap? map) && map is not null)
            {
                if (!EnterCollection(item!, depth))
                {
                    return;
                }

                Add(checked(5UL + ((ulong)map.Count * 2UL) - (map.Count > 0 ? 1UL : 0UL)));
                IEnumerator<KeyValuePair<object?, object?>> enumerator = map.GetEnumerator();
                activeEnumerators.Add(enumerator);
                work.Push(EstimateFrame.Exit(item!));
                work.Push(EstimateFrame.MapValue(enumerator, depth));
                return;
            }

            Add((ulong)FormatScalar(item).Length);
        }

        bool EnterCollection(object identity, int depth)
        {
            if (depth >= maximumDepth)
            {
                throw new ExprRuntimeException($"string formatting exceeds maximum depth of {maximumDepth}");
            }

            if (!activeCollections.Add(identity))
            {
                Add(7);
                return false;
            }

            return true;
        }

        void Add(ulong amount)
        {
            length = checked(length + amount);
            if (length > maximumLength)
            {
                throw new ExprRuntimeException($"string formatting exceeds maximum length of {maximumLength}");
            }
        }
    }

    private static void AppendBounded(StringBuilder builder, string value, int maximumLength)
    {
        if (value.Length > maximumLength - builder.Length)
        {
            throw new ExprRuntimeException($"string formatting exceeds maximum length of {maximumLength}");
        }

        _ = builder.Append(value);
    }

    private enum FormatFrameKind
    {
        Value,
        Text,
        ExitCollection,
    }

    private enum EstimateFrameKind
    {
        Value,
        Array,
        Map,
        Exit,
    }

    private readonly record struct EstimateFrame(
        EstimateFrameKind Kind,
        object? Item,
        IExprArray? Array,
        IEnumerator<KeyValuePair<object?, object?>>? Map,
        int Index,
        int Depth)
    {
        internal static EstimateFrame Value(object? item, int depth) =>
            new(EstimateFrameKind.Value, item, null, null, 0, depth);

        internal static EstimateFrame ArrayValue(IExprArray array, int depth) =>
            new(EstimateFrameKind.Array, null, array, null, 0, depth);

        internal static EstimateFrame MapValue(
            IEnumerator<KeyValuePair<object?, object?>> map,
            int depth) => new(EstimateFrameKind.Map, null, null, map, 0, depth);

        internal static EstimateFrame Exit(object item) =>
            new(EstimateFrameKind.Exit, item, null, null, 0, 0);
    }

    private readonly record struct FormatFrame(FormatFrameKind Kind, object? Item, string? Text, int Depth)
    {
        internal static FormatFrame Value(object? item, int depth) => new(FormatFrameKind.Value, item, null, depth);

        internal static FormatFrame TextValue(string text) => new(FormatFrameKind.Text, null, text, 0);

        internal static FormatFrame Exit(object item) => new(FormatFrameKind.ExitCollection, item, null, 0);
    }

    private static ExprRuntimeException Error(string name, object? value) =>
        new($"invalid argument for {name} (type {TypeNameOf(value)})");
}
