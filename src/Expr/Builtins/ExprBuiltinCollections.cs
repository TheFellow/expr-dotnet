using System;
using System.Collections.Generic;
using System.Linq;
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
        IExprArray array = RequireArray(arguments[0], "first");
        return array.Count == 0 ? null : array[0];
    }

    public static object? Last(ReadOnlySpan<object?> arguments)
    {
        IExprArray array = RequireArray(arguments[0], "last");
        return array.Count == 0 ? null : array[array.Count - 1];
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
            long requested = ExprBuiltinValues.RequireInteger(key, "get");
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
            new ExprArray(map.Select(static pair => (object?)new ExprArray([pair.Key, pair.Value]))),
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
        IExprArray array = RequireArray(arguments[0], "sort");
        EnsureAllocation(array.Count, options);
        bool descending = arguments.Length == 2 && ParseOrder(arguments[1]) == "desc";
        object?[] values = array.ToArray();
        Array.Sort(values, (left, right) => descending ? CompareForSort(right, left) : CompareForSort(left, right));
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
        if (ExprCollections.TryAsArray(key, out _) || ExprCollections.TryAsMap(key, out _))
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
