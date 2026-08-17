using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Execution;
using Expr.Runtime;

namespace Expr.Builtins;

internal static class ExprBuiltinCollections
{
    public static object? MinMax(ReadOnlySpan<object?> arguments, bool maximum, ExprBuiltinOptions options)
    {
        if (arguments.IsEmpty)
        {
            throw new ExprRuntimeException($"not enough arguments to call {(maximum ? "max" : "min")}");
        }

        if (arguments.Length is 1 &&
            !ExprBuiltinValues.IsNumeric(arguments[0]) &&
            !ExprCollections.TryAsArray(arguments[0], out _))
        {
            // Upstream's dynamic aggregate path returns a lone non-array value unchanged.
            // Static validation still rejects known incompatible types before invocation.
            return arguments[0];
        }

        object? selected = null;
        bool found = false;
        VisitNested(arguments, options, (value, _) =>
        {
            if (!ExprBuiltinValues.IsNumeric(value))
            {
                throw InvalidAggregate(maximum ? "max" : "min", value);
            }

            if (!found || (maximum ? ExprValue.Less(selected, value) : ExprValue.Less(value, selected)))
            {
                selected = value;
                found = true;
            }
        });
        return selected;
    }

    public static object Mean(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        if (arguments.IsEmpty)
        {
            throw new ExprRuntimeException("not enough arguments to call mean");
        }

        double total = 0;
        long count = 0;
        VisitNested(arguments, options, (value, _) =>
        {
            if (!ExprBuiltinValues.IsNumeric(value))
            {
                throw InvalidAggregate("mean", value);
            }

            total += ExprValue.ToDouble(value);
            count++;
        });
        return count == 0 ? 0D : total / count;
    }

    public static object Median(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        if (arguments.IsEmpty)
        {
            throw new ExprRuntimeException("not enough arguments to call median");
        }

        var values = new List<double>();
        VisitNested(arguments, options, (value, _) =>
        {
            if (!ExprBuiltinValues.IsNumeric(value))
            {
                throw InvalidAggregate("median", value);
            }

            if (values.Count >= options.MaximumAllocation)
            {
                throw new ExprRuntimeException("memory budget exceeded");
            }

            values.Add(ExprValue.ToDouble(value));
        });
        if (values.Count == 0)
        {
            return 0D;
        }

        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    public static object? First(ReadOnlySpan<object?> arguments)
    {
        object? value = arguments[0];
        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            return array.Count == 0 ? null : array[0];
        }

        if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
        {
            if (!ExprCollections.TryConvertIntegerKey(0L, map.KeyType, out object? key))
            {
                return null;
            }

            return map.TryGetValue(key, out object? first)
                ? first
                : ExprCollections.GetMapDefaultValue(map);
        }

        return null;
    }

    public static object? Last(ReadOnlySpan<object?> arguments)
    {
        object? value = arguments[0];
        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            return array.Count == 0 ? null : array[^1];
        }

        if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
        {
            if (!ExprCollections.TryConvertIntegerKey(-1L, map.KeyType, out object? key))
            {
                return null;
            }

            return map.TryGetValue(key, out object? last)
                ? last
                : ExprCollections.GetMapDefaultValue(map);
        }

        return null;
    }

    public static object? Get(ReadOnlySpan<object?> arguments)
    {
        object? from = arguments[0];
        object? key = arguments[1];
        if (from is null)
        {
            return null;
        }

        if (ExprCollections.TryAsArray(from, out IExprArray? array) && array is not null)
        {
            long requested = ExprValue.ToInt64(key);
            long index = requested < 0 ? array.Count + requested : requested;
            return index >= 0 && index < array.Count ? array[(int)index] : null;
        }

        if (from is string text)
        {
            long requested = ExprBuiltinValues.RequireInteger(key, "get");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            long index = requested < 0 ? bytes.Length + requested : requested;
            return index >= 0 && index < bytes.Length ? bytes[(int)index] : null;
        }

        if (ExprCollections.TryAsMap(from, out IExprMap? map) && map is not null)
        {
            return map.TryGetValue(key, out object? value) ? value : null;
        }

        return null;
    }

    public static ExprInvocationResult Take(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprArray array = RequireArray(arguments[0], "take");
        long requested = ExprBuiltinValues.RequireInteger(arguments[1], "take");
        if (requested < 0)
        {
            throw new ExprRuntimeException($"cannot take {requested} elements");
        }

        int count = (int)Math.Min(requested, array.Count);
        EnsureAllocation(count, options);
        var values = new object?[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = array[index];
        }

        return Result(new ExprArray(values), count);
    }

    public static ExprInvocationResult Keys(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprMap map = RequireMap(arguments[0], "get keys from");
        EnsureAllocation(map.Count, options);
        return Result(new ExprArray(map.Select(static pair => pair.Key)), map.Count);
    }

    public static ExprInvocationResult Values(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprMap map = RequireMap(arguments[0], "get values from");
        EnsureAllocation(map.Count, options);
        return Result(new ExprArray(map.Select(static pair => pair.Value)), map.Count);
    }

    public static ExprInvocationResult ToPairs(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprMap map = RequireMap(arguments[0], "transform to pairs");
        EnsureAllocation(map.Count, options);
        return Result(
            new ExprTypedArray(
                map.Select(static pair => (object?)new ExprArray([pair.Key, pair.Value])),
                typeof(IExprArray)),
            map.Count);
    }

    public static ExprInvocationResult FromPairs(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprArray pairs = RequireArray(arguments[0], "transform from pairs");
        EnsureAllocation(pairs.Count, options);
        var values = new List<KeyValuePair<object?, object?>>(pairs.Count);
        for (int index = 0; index < pairs.Count; index++)
        {
            if (!ExprCollections.TryAsArray(pairs[index], out IExprArray? pair) || pair is null)
            {
                throw new ExprRuntimeException($"invalid pair {pairs[index]}");
            }

            if (pair.Count != 2)
            {
                throw new ExprRuntimeException($"invalid pair length {pair.Count}");
            }

            EnsureMapKey(pair[0]);

            int existing = values.FindIndex(entry => ExprValue.Equal(entry.Key, pair[0]));
            var entry = new KeyValuePair<object?, object?>(pair[0], pair[1]);
            if (existing >= 0)
            {
                values[existing] = entry;
            }
            else
            {
                values.Add(entry);
            }
        }

        return Result(new ExprMap(values), values.Count);
    }

    public static ExprInvocationResult Reverse(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprArray array = RequireArray(arguments[0], "reverse");
        EnsureAllocation(array.Count, options);
        var values = new object?[array.Count];
        for (int index = 0; index < array.Count; index++)
        {
            values[index] = array[array.Count - index - 1];
        }

        return Result(new ExprArray(values), values.Length);
    }

    public static ExprInvocationResult Unique(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprArray array = RequireArray(arguments[0], "uniq");
        var values = new List<object?>(array.Count);
        foreach (object? value in array)
        {
            if (!values.Exists(existing => ExprValue.Equal(existing, value)))
            {
                EnsureAllocation(values.Count + 1, options);
                values.Add(value);
            }
        }

        return Result(new ExprArray(values), values.Count);
    }

    public static ExprInvocationResult Concat(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        if (arguments.IsEmpty)
        {
            throw new ExprRuntimeException("invalid number of arguments (expected at least 1, got 0)");
        }

        var arrays = new IExprArray[arguments.Length];
        long count = 0;
        for (int index = 0; index < arguments.Length; index++)
        {
            arrays[index] = RequireArray(arguments[index], "concat");
            count = checked(count + arrays[index].Count);
            EnsureAllocation(count, options);
        }

        var values = new List<object?>((int)count);
        foreach (IExprArray array in arrays)
        {
            values.AddRange(array);
        }

        return Result(new ExprArray(values), count);
    }

    public static ExprInvocationResult Flatten(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprArray root = RequireArray(arguments[0], "flatten");
        var values = new List<object?>();
        var pending = new Stack<(object? Value, int Depth)>();
        for (int index = root.Count - 1; index >= 0; index--)
        {
            pending.Push((root[index], 0));
        }

        while (pending.TryPop(out (object? Value, int Depth) item))
        {
            if (item.Depth > options.MaximumDepth)
            {
                throw new ExprRuntimeException("recursion depth exceeded");
            }

            if (ExprCollections.TryAsArray(item.Value, out IExprArray? nested) && nested is not null)
            {
                for (int index = nested.Count - 1; index >= 0; index--)
                {
                    pending.Push((nested[index], item.Depth + 1));
                }
            }
            else
            {
                EnsureAllocation(values.Count + 1, options);
                values.Add(item.Value);
            }
        }

        return Result(new ExprArray(values), values.Count);
    }

    public static ExprInvocationResult Sort(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        IExprArray array = ExprCollections.TryAsArray(arguments[0], out IExprArray? adapted) &&
            adapted is not null && IsSupportedSortArray(adapted)
            ? adapted
            : ExprNilArray.Instance;
        EnsureAllocation(array.Count, options);
        bool descending = arguments.Length == 2 && ParseOrder(arguments[1]) == "desc";
        if (array is IExprNilValue)
        {
            return Result(ExprNilArray.Instance, 0);
        }

        object?[] values = [.. array];
        SortValues(values, descending);
        return Result(new ExprArray(values), values.Length);
    }

    internal static IExprArray RequireArray(object? value, string operation)
    {
        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            return array;
        }

        throw new ExprRuntimeException($"cannot {operation} {ExprBuiltinValues.TypeNameOf(value)}");
    }

    internal static string ParseOrder(object? value)
    {
        if (value is not string order)
        {
            throw new ExprRuntimeException(
                $"sort order argument must be a string (got {ExprBuiltinValues.TypeNameOf(value)})");
        }

        if (order is not ("asc" or "desc"))
        {
            throw new ExprRuntimeException($"invalid order {order}, expected asc or desc");
        }

        return order;
    }

    internal static void EnsureAllocation(long count, ExprBuiltinOptions options)
    {
        if (count < 0 || count > options.MaximumAllocation || count > int.MaxValue)
        {
            throw new ExprRuntimeException("memory budget exceeded");
        }
    }

    internal static void EnsureMapKey(object? key)
    {
        if (!ExprExecutionOperations.IsValidMapKey(key))
        {
            throw new ExprRuntimeException(
                $"runtime error: hash of unhashable type {ExprBuiltinValues.TypeNameOf(key)}");
        }
    }

    internal static int CompareForSort(object? left, object? right)
    {
        if (ExprValue.Less(left, right))
        {
            return -1;
        }

        return ExprValue.Less(right, left) ? 1 : 0;
    }

    private static void SortValues(object?[] values, bool descending)
    {
        if (values.Length <= 12)
        {
            // Go's sort package uses insertion sort for slices up to twelve elements.
            // Matching that comparison order matters for dynamic heterogeneous values:
            // an invalid comparison must fail only when upstream performs it too.
            for (var index = 1; index < values.Length; index++)
            {
                for (int current = index; current > 0; current--)
                {
                    int comparison = descending
                        ? CompareForSort(values[current - 1], values[current])
                        : CompareForSort(values[current], values[current - 1]);
                    if (comparison >= 0)
                    {
                        break;
                    }

                    (values[current - 1], values[current]) = (values[current], values[current - 1]);
                }
            }

            return;
        }

        Array.Sort(values, (left, right) => descending ? CompareForSort(right, left) : CompareForSort(left, right));
    }

    private static bool IsSupportedSortArray(IExprArray array)
    {
        Type elementType = array.ElementType;
        if (elementType == typeof(object) || elementType == typeof(string))
        {
            return true;
        }

        return Type.GetTypeCode(elementType) is
            TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
            TypeCode.Single or TypeCode.Double;
    }

    private static IExprMap RequireMap(object? value, string operation)
    {
        if (ExprCollections.TryAsMap(value, out IExprMap? map) && map is not null)
        {
            return map;
        }

        throw new ExprRuntimeException($"cannot {operation} {ExprBuiltinValues.TypeNameOf(value)}");
    }

    private static ExprInvocationResult Result(object? value, long cost) => new(value, checked((ulong)cost));

    private static void VisitNested(
        ReadOnlySpan<object?> arguments,
        ExprBuiltinOptions options,
        Action<object?, int> visitor)
    {
        var pending = new Stack<(object? Value, int Depth)>();
        for (int index = arguments.Length - 1; index >= 0; index--)
        {
            pending.Push((arguments[index], 0));
        }

        while (pending.TryPop(out (object? Value, int Depth) item))
        {
            if (item.Depth > options.MaximumDepth)
            {
                throw new ExprRuntimeException("recursion depth exceeded");
            }

            if (ExprCollections.TryAsArray(item.Value, out IExprArray? nested) && nested is not null)
            {
                for (int index = nested.Count - 1; index >= 0; index--)
                {
                    pending.Push((nested[index], item.Depth + 1));
                }
            }
            else
            {
                visitor(item.Value, item.Depth);
            }
        }
    }

    private static ExprRuntimeException InvalidAggregate(string name, object? value) =>
        new($"invalid argument for {name} (type {ExprBuiltinValues.TypeNameOf(value)})");
}
