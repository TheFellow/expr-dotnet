using System;
using System.Collections.Generic;
using Expr.Runtime;

namespace Expr.Builtins;

internal static class ExprBuiltinPredicates
{
    public static ExprInvocationResult Invoke(
        string name,
        object? collection,
        ExprBuiltinPredicateContext context,
        ExprBuiltinOptions options)
    {
        IExprArray array = ExprBuiltinCollections.RequireArray(collection, name);
        return name switch
        {
            "all" => Result(All(array, context.Predicate), 0),
            "none" => Result(!Any(array, context.Predicate), 0),
            "any" => Result(Any(array, context.Predicate), 0),
            "one" => Result(One(array, context.Predicate), 0),
            "filter" => Filter(array, context.Predicate, options),
            "map" => Map(array, context.Predicate, options),
            "find" => Result(Find(array, context.Predicate, last: false, returnIndex: false), 0),
            "findIndex" => Result(Find(array, context.Predicate, last: false, returnIndex: true), 0),
            "findLast" => Result(Find(array, context.Predicate, last: true, returnIndex: false), 0),
            "findLastIndex" => Result(Find(array, context.Predicate, last: true, returnIndex: true), 0),
            "count" => Result(Count(array, context.Predicate), 0),
            "sum" => Result(Sum(array, context.Predicate), 0),
            "groupBy" => GroupBy(array, context.Predicate, options),
            "sortBy" => SortBy(array, context, options),
            "reduce" => Result(Reduce(array, context), 0),
            _ => throw new ExprRuntimeException($"function {name} is not a predicate builtin"),
        };
    }

    private static bool All(IExprArray array, ExprBuiltinPredicate predicate)
    {
        for (int index = 0; index < array.Count; index++)
        {
            if (!RequireBoolean(predicate(array[index], index, null)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Any(IExprArray array, ExprBuiltinPredicate predicate)
    {
        for (int index = 0; index < array.Count; index++)
        {
            if (RequireBoolean(predicate(array[index], index, null)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool One(IExprArray array, ExprBuiltinPredicate predicate)
    {
        bool matched = false;
        for (int index = 0; index < array.Count; index++)
        {
            if (!RequireBoolean(predicate(array[index], index, null)))
            {
                continue;
            }

            if (matched)
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }

    private static ExprInvocationResult Filter(
        IExprArray array,
        ExprBuiltinPredicate predicate,
        ExprBuiltinOptions options)
    {
        var values = new List<object?>();
        for (int index = 0; index < array.Count; index++)
        {
            if (RequireBoolean(predicate(array[index], index, null)))
            {
                ExprBuiltinCollections.EnsureAllocation(values.Count + 1, options);
                values.Add(array[index]);
            }
        }

        return Result(new ExprArray(values), values.Count);
    }

    private static ExprInvocationResult Map(
        IExprArray array,
        ExprBuiltinPredicate predicate,
        ExprBuiltinOptions options)
    {
        ExprBuiltinCollections.EnsureAllocation(array.Count, options);
        var values = new object?[array.Count];
        for (int index = 0; index < array.Count; index++)
        {
            values[index] = predicate(array[index], index, null);
        }

        return Result(new ExprArray(values), values.Length);
    }

    private static object? Find(
        IExprArray array,
        ExprBuiltinPredicate predicate,
        bool last,
        bool returnIndex)
    {
        int start = last ? array.Count - 1 : 0;
        int end = last ? -1 : array.Count;
        int increment = last ? -1 : 1;
        for (int index = start; index != end; index += increment)
        {
            if (RequireBoolean(predicate(array[index], index, null)))
            {
                return returnIndex ? (long)index : array[index];
            }
        }

        return returnIndex ? -1L : null;
    }

    private static long Count(IExprArray array, ExprBuiltinPredicate predicate)
    {
        long count = 0;
        for (int index = 0; index < array.Count; index++)
        {
            if (RequireBoolean(predicate(array[index], index, null)))
            {
                count++;
            }
        }

        return count;
    }

    private static object Sum(IExprArray array, ExprBuiltinPredicate predicate)
    {
        long integers = 0;
        double floating = 0;
        bool hasFloat = false;
        for (int index = 0; index < array.Count; index++)
        {
            object? value = predicate(array[index], index, null);
            if (!ExprBuiltinValues.IsNumeric(value))
            {
                throw new ExprRuntimeException(
                    $"invalid argument for sum (type {ExprBuiltinValues.TypeNameOf(value)})");
            }

            if (value is Half or float or double)
            {
                if (!hasFloat)
                {
                    floating = integers;
                    hasFloat = true;
                }

                floating += ExprValue.ToDouble(value);
            }
            else if (hasFloat)
            {
                floating += ExprValue.ToDouble(value);
            }
            else
            {
                integers = unchecked(integers + ExprValue.ToInt64(value));
            }
        }

        if (hasFloat)
        {
            return floating;
        }

        return integers;
    }

    private static ExprInvocationResult GroupBy(
        IExprArray array,
        ExprBuiltinPredicate predicate,
        ExprBuiltinOptions options)
    {
        var groups = new List<(object? Key, List<object?> Values)>();
        for (int index = 0; index < array.Count; index++)
        {
            object? key = predicate(array[index], index, null);
            ExprBuiltinCollections.EnsureMapKey(key);
            int groupIndex = groups.FindIndex(group => ExprValue.Equal(group.Key, key));
            if (groupIndex < 0)
            {
                ExprBuiltinCollections.EnsureAllocation(groups.Count + 1, options);
                groups.Add((key, [array[index]]));
            }
            else
            {
                groups[groupIndex].Values.Add(array[index]);
            }
        }

        var entries = new KeyValuePair<object?, object?>[groups.Count];
        for (int index = 0; index < groups.Count; index++)
        {
            entries[index] = new KeyValuePair<object?, object?>(
                groups[index].Key,
                new ExprArray(groups[index].Values));
        }

        return Result(new ExprMap(entries), array.Count);
    }

    private static ExprInvocationResult SortBy(
        IExprArray array,
        ExprBuiltinPredicateContext context,
        ExprBuiltinOptions options)
    {
        ExprBuiltinCollections.EnsureAllocation(array.Count, options);
        bool descending = ExprBuiltinCollections.ParseOrder(context.SortOrder) == "desc";
        var values = new (object? Item, object? Key, int Index)[array.Count];
        for (int index = 0; index < array.Count; index++)
        {
            values[index] = (array[index], context.Predicate(array[index], index, null), index);
        }

        Array.Sort(values, (left, right) =>
        {
            int comparison = descending
                ? ExprValue.Compare(right.Key, left.Key)
                : ExprValue.Compare(left.Key, right.Key);
            return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
        });
        var sorted = new object?[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            sorted[index] = values[index].Item;
        }

        return Result(new ExprArray(sorted), sorted.Length);
    }

    private static object? Reduce(IExprArray array, ExprBuiltinPredicateContext context)
    {
        object? accumulator;
        int start;
        if (context.HasInitialValue)
        {
            accumulator = context.InitialValue;
            start = 0;
        }
        else if (array.Count > 0)
        {
            accumulator = array[0];
            start = 1;
        }
        else
        {
            return null;
        }

        for (int index = start; index < array.Count; index++)
        {
            accumulator = context.Predicate(array[index], index, accumulator);
        }

        return accumulator;
    }

    private static bool RequireBoolean(object? value) => value is bool boolean
        ? boolean
        : throw new ExprRuntimeException(
            $"predicate should return bool (got {ExprBuiltinValues.TypeNameOf(value)})");

    private static ExprInvocationResult Result(object? value, long cost) => new(value, checked((ulong)cost));
}
