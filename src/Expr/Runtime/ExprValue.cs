using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Expr.Runtime;

/// <summary>
/// Implements host-value classification, conversion, equality, and ordering with Expr semantics.
/// </summary>
public static class ExprValue
{
    /// <summary>Gets the maximum nesting depth inspected by deep equality.</summary>
    public const int MaximumEqualityDepth = 10_000;

    /// <summary>Classifies a host value into an Expr runtime category.</summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The runtime category.</returns>
    public static ExprValueKind Classify(object? value) => value switch
    {
        null => ExprValueKind.Nil,
        bool => ExprValueKind.Boolean,
        sbyte or short or int or long or nint => ExprValueKind.SignedInteger,
        byte or ushort or uint or ulong or nuint => ExprValueKind.UnsignedInteger,
        Half or float or double => ExprValueKind.Float,
        string => ExprValueKind.String,
        DateTime or DateTimeOffset => ExprValueKind.Time,
        TimeSpan => ExprValueKind.Duration,
        Delegate => ExprValueKind.Function,
        _ when ExprCollections.TryAsArray(value, out _) => ExprValueKind.Array,
        _ when ExprCollections.TryAsMap(value, out _) => ExprValueKind.Map,
        _ => ExprValueKind.Object,
    };

    /// <summary>Converts a supported numeric value to Expr's canonical signed integer.</summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>The converted integer.</returns>
    /// <exception cref="ExprRuntimeException">The value is not numeric.</exception>
    public static long ToInt64(object? value) => value switch
    {
        sbyte number => number,
        byte number => number,
        short number => number,
        ushort number => number,
        int number => number,
        uint number => number,
        long number => number,
        ulong number => unchecked((long)number),
        nint number => number,
        nuint number => unchecked((long)number),
        Half number => (long)number,
        float number => (long)number,
        double number => (long)number,
        _ => throw InvalidConversion("int", value),
    };

    /// <summary>Converts a supported numeric value to Expr's canonical floating-point value.</summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>The converted floating-point value.</returns>
    /// <exception cref="ExprRuntimeException">The value is not numeric.</exception>
    public static double ToDouble(object? value) => value switch
    {
        sbyte number => number,
        byte number => number,
        short number => number,
        ushort number => number,
        int number => number,
        uint number => number,
        long number => number,
        ulong number => number,
        nint number => number,
        nuint number => number,
        Half number => (double)number,
        float number => number,
        double number => number,
        _ => throw InvalidConversion("float", value),
    };

    /// <summary>Converts a value to Boolean using Expr's strict conversion rule.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns><see langword="false"/> for nil; otherwise the Boolean value.</returns>
    /// <exception cref="ExprRuntimeException">A non-Boolean, non-nil value was supplied.</exception>
    public static bool ToBoolean(object? value) => value switch
    {
        null => false,
        bool boolean => boolean,
        _ => throw InvalidConversion("bool", value),
    };

    /// <summary>Determines whether two values are equal under Expr semantics.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool Equal(object? left, object? right)
    {
        var pending = new Stack<EqualityWorkItem>();
        var visited = new HashSet<ReferencePair>(ReferencePairComparer.Instance);
        pending.Push(new EqualityWorkItem(left, right, 0));

        while (pending.TryPop(out EqualityWorkItem item))
        {
            if (!EqualOne(item, pending, visited))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares two values under Expr ordering rules.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>A negative value, zero, or a positive value when left is less than, equal to, or greater than right.</returns>
    /// <exception cref="ExprRuntimeException">The values are not orderable with each other.</exception>
    public static int Compare(object? left, object? right)
    {
        ExprValueKind leftKind = Classify(left);
        ExprValueKind rightKind = Classify(right);
        if (IsNumeric(leftKind) && IsNumeric(rightKind))
        {
            if (leftKind is ExprValueKind.Float || rightKind is ExprValueKind.Float)
            {
                double leftNumber = ToDouble(left);
                double rightNumber = ToDouble(right);
                if (double.IsNaN(leftNumber) || double.IsNaN(rightNumber))
                {
                    throw new ExprRuntimeException("NaN values are unordered");
                }

                return leftNumber.CompareTo(rightNumber);
            }

            return ToInt64(left).CompareTo(ToInt64(right));
        }

        if (left is string leftString && right is string rightString)
        {
            return CompareUtf8(leftString, rightString);
        }

        if (TryGetInstant(left, out DateTimeOffset leftTime) && TryGetInstant(right, out DateTimeOffset rightTime))
        {
            return leftTime.UtcTicks.CompareTo(rightTime.UtcTicks);
        }

        if (left is TimeSpan leftDuration && right is TimeSpan rightDuration)
        {
            return leftDuration.CompareTo(rightDuration);
        }

        throw new ExprRuntimeException($"invalid operation: {TypeName(left)} < {TypeName(right)}");
    }

    /// <summary>Determines whether the left value sorts before the right value.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when left is less than right.</returns>
    public static bool Less(object? left, object? right)
    {
        ExprValueKind leftKind = Classify(left);
        ExprValueKind rightKind = Classify(right);
        if (IsNumeric(leftKind) && IsNumeric(rightKind) &&
            (leftKind is ExprValueKind.Float || rightKind is ExprValueKind.Float))
        {
            return ToDouble(left) < ToDouble(right);
        }

        return Compare(left, right) < 0;
    }

    /// <summary>Gets the VM storage length of an Expr array, map, or string.</summary>
    /// <remarks>
    /// Strings are counted as UTF-8 bytes for indexing parity with Go. The Expr <c>len</c>
    /// builtin counts Unicode scalar values and must not use this helper for strings.
    /// </remarks>
    /// <param name="value">The value.</param>
    /// <returns>The number of elements or UTF-8 bytes, matching Go string length.</returns>
    /// <exception cref="ExprRuntimeException">The value has no length.</exception>
    public static int StorageLength(object? value)
    {
        if (value is string text)
        {
            return Encoding.UTF8.GetByteCount(text);
        }

        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            return array.Count;
        }

        if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
        {
            return map.Count;
        }

        throw new ExprRuntimeException($"invalid argument for len (type {TypeName(value)})");
    }

    /// <summary>Fetches an array or string element using Expr's negative-index rule.</summary>
    /// <param name="value">The array or string.</param>
    /// <param name="index">The possibly negative index.</param>
    /// <returns>The selected element, or a UTF-8 byte when indexing a string.</returns>
    /// <exception cref="ExprRuntimeException">The value is not indexable or the index is out of range.</exception>
    public static object? FetchIndex(object? value, long index)
    {
        int length = StorageLength(value);
        long actual = index < 0 ? length + index : index;
        if (actual < 0 || actual >= length)
        {
            throw new ExprRuntimeException($"index out of range: {actual} (array length is {length})");
        }

        if (value is string text)
        {
            return Encoding.UTF8.GetBytes(text)[(int)actual];
        }

        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            return array[(int)actual];
        }

        throw new ExprRuntimeException($"cannot fetch {index} from {TypeName(value)}");
    }

    /// <summary>Tests collection membership with Expr equality.</summary>
    /// <param name="needle">The value or key being sought.</param>
    /// <param name="collection">The array or map.</param>
    /// <returns><see langword="true"/> when the value is present.</returns>
    /// <exception cref="ExprRuntimeException">The second operand is not a collection.</exception>
    public static bool In(object? needle, object? collection)
    {
        if (collection is null)
        {
            return false;
        }

        if (ExprCollections.TryAsArray(collection, out IExprArray? array) && array is not null)
        {
            foreach (object? item in array)
            {
                if (Equal(item, needle))
                {
                    return true;
                }
            }

            return false;
        }

        if (ExprCollections.TryAsMap(collection, out IExprMap? map) && map is not null)
        {
            return map.TryGetValue(needle, out _);
        }

        throw new ExprRuntimeException($"operator \"in\" not defined on {TypeName(collection)}");
    }

    private static bool EqualOne(
        EqualityWorkItem item,
        Stack<EqualityWorkItem> pending,
        HashSet<ReferencePair> visited)
    {
        object? left = item.Left;
        object? right = item.Right;
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        ExprValueKind leftKind = Classify(left);
        ExprValueKind rightKind = Classify(right);
        if (IsNumeric(leftKind) && IsNumeric(rightKind))
        {
            return leftKind is ExprValueKind.Float || rightKind is ExprValueKind.Float
                ? ToDouble(left) == ToDouble(right)
                : ToInt64(left) == ToInt64(right);
        }

        if (left is string leftString && right is string rightString)
        {
            return string.Equals(leftString, rightString, StringComparison.Ordinal);
        }

        if (left is bool leftBoolean && right is bool rightBoolean)
        {
            return leftBoolean == rightBoolean;
        }

        if (TryGetInstant(left, out DateTimeOffset leftTime) && TryGetInstant(right, out DateTimeOffset rightTime))
        {
            return leftTime.UtcTicks == rightTime.UtcTicks;
        }

        if (left is TimeSpan leftDuration && right is TimeSpan rightDuration)
        {
            return leftDuration == rightDuration;
        }

        if (item.Depth >= MaximumEqualityDepth)
        {
            throw new ExprRuntimeException("recursion depth exceeded");
        }

        bool leftArray = ExprCollections.TryAsArray(left, out IExprArray? leftItems);
        bool rightArray = ExprCollections.TryAsArray(right, out IExprArray? rightItems);
        if (leftArray || rightArray)
        {
            if (!leftArray || !rightArray || leftItems!.Count != rightItems!.Count)
            {
                return false;
            }

            if (leftItems.ElementType != typeof(object) &&
                rightItems.ElementType != typeof(object) &&
                leftItems.ElementType != rightItems.ElementType)
            {
                return false;
            }

            if (!visited.Add(new ReferencePair(left, right)))
            {
                return true;
            }

            for (int index = 0; index < leftItems.Count; index++)
            {
                pending.Push(new EqualityWorkItem(leftItems[index], rightItems[index], item.Depth + 1));
            }

            return true;
        }

        bool leftMap = ExprCollections.TryAsMap(left, out IExprMap? leftEntries);
        bool rightMap = ExprCollections.TryAsMap(right, out IExprMap? rightEntries);
        if (leftMap || rightMap)
        {
            if (!leftMap || !rightMap || leftEntries!.Count != rightEntries!.Count)
            {
                return false;
            }

            if (!visited.Add(new ReferencePair(left, right)))
            {
                return true;
            }

            foreach ((object? key, object? value) in leftEntries)
            {
                if (!rightEntries.TryGetValue(key, out object? otherValue))
                {
                    return false;
                }


                pending.Push(new EqualityWorkItem(value, otherValue, item.Depth + 1));
            }

            return true;
        }

        return left.Equals(right);
    }

    private static bool IsNumeric(ExprValueKind kind) =>
        kind is ExprValueKind.SignedInteger or ExprValueKind.UnsignedInteger or ExprValueKind.Float;

    private static int CompareUtf8(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
    }

    private static bool TryGetInstant(object? value, out DateTimeOffset instant)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                instant = offset;
                return true;
            case DateTime dateTime:
                instant = dateTime.Kind switch
                {
                    DateTimeKind.Local => new DateTimeOffset(dateTime),
                    DateTimeKind.Utc => new DateTimeOffset(dateTime, TimeSpan.Zero),
                    _ => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc), TimeSpan.Zero),
                };
                return true;
            default:
                instant = default;
                return false;
        }
    }

    private static ExprRuntimeException InvalidConversion(string target, object? value) =>
        new($"invalid operation: {target}({TypeName(value)})");

    private static string TypeName(object? value) => value?.GetType().FullName ?? "nil";

    private readonly record struct ReferencePair(object Left, object Right);

    private readonly record struct EqualityWorkItem(object? Left, object? Right, int Depth);

    private sealed class ReferencePairComparer : IEqualityComparer<ReferencePair>
    {
        public static ReferencePairComparer Instance { get; } = new();

        public bool Equals(ReferencePair x, ReferencePair y) =>
            ReferenceEquals(x.Left, y.Left) && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode(ReferencePair obj) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Left), RuntimeHelpers.GetHashCode(obj.Right));
    }
}
