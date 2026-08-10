using System;
using System.Linq;
using Expr.Builtins;
using Expr.Runtime;
using Xunit;

// Inline arrays keep predicate input and expected values adjacent in these non-hot-path tests.
#pragma warning disable CA1861

namespace Expr.Tests.Builtins;

public sealed class BuiltinPredicateTests
{
    private readonly ExprBuiltinLibrary library = new();

    [Fact]
    public void Boolean_predicates_short_circuit_and_handle_empty_arrays()
    {
        int calls = 0;
        var positive = new ExprBuiltinPredicateContext((value, _, _) =>
        {
            calls++;
            return (long)value! > 0;
        });

        Assert.Equal(true, Invoke("all", Array.Empty<long>(), positive));
        Assert.Equal(false, Invoke("any", Array.Empty<long>(), positive));
        Assert.Equal(true, Invoke("none", Array.Empty<long>(), positive));
        Assert.Equal(true, Invoke("any", new long[] { 1, -1, 2 }, positive));
        Assert.Equal(1, calls);
        Assert.Equal(false, Invoke("one", new long[] { 1, -1, 2 }, positive));
    }

    [Fact]
    public void Filter_map_find_and_count_expose_value_and_index()
    {
        long[] values = [10, 11, 12, 13];
        var evenIndex = new ExprBuiltinPredicateContext((_, index, _) => index % 2 == 0);
        var square = new ExprBuiltinPredicateContext((value, _, _) => (long)value! * (long)value!);

        Assert.Equal([10L, 12L], Items(Invoke("filter", values, evenIndex)));
        Assert.Equal([100L, 121L, 144L, 169L], Items(Invoke("map", values, square)));
        Assert.Equal(12L, Invoke("find", values, new((value, _, _) => (long)value! > 11)));
        Assert.Equal(3L, Invoke("findLastIndex", values, new((value, _, _) => (long)value! > 11)));
        Assert.Equal(2L, Invoke("count", values, evenIndex));
    }

    [Fact]
    public void Sum_group_sort_and_reduce_match_upstream_scope_rules()
    {
        long[] values = [1, 2, 3, 4];
        var identity = new ExprBuiltinPredicateContext((value, _, _) => value);
        var parity = new ExprBuiltinPredicateContext((value, _, _) => (long)value! % 2);
        var descendingKey = new ExprBuiltinPredicateContext((value, _, _) => -(long)value!);
        var reduce = new ExprBuiltinPredicateContext((value, _, accumulator) => (long)value! + (long)accumulator!)
        {
            HasInitialValue = true,
            InitialValue = 10L,
        };

        Assert.Equal(10L, Invoke("sum", values, identity));
        IExprMap groups = Assert.IsAssignableFrom<IExprMap>(Invoke("groupBy", values, parity));
        Assert.True(groups.TryGetValue(0L, out object? evens));
        Assert.Equal([2L, 4L], Items(evens));
        Assert.Equal([4L, 3L, 2L, 1L], Items(Invoke("sortBy", values, descendingKey)));
        Assert.Equal(20L, Invoke("reduce", values, reduce));
    }

    [Fact]
    public void Reduce_without_initial_value_uses_first_element_and_empty_returns_nil()
    {
        var reduce = new ExprBuiltinPredicateContext((value, _, accumulator) => (long)value! + (long)accumulator!);

        Assert.Equal(6L, Invoke("reduce", new long[] { 1, 2, 3 }, reduce));
        Assert.Null(Invoke("reduce", Array.Empty<long>(), reduce));
    }

    [Fact]
    public void Predicate_requires_boolean_for_boolean_operations()
    {
        var invalid = new ExprBuiltinPredicateContext((value, _, _) => value);

        Assert.Throws<ExprRuntimeException>(() => Invoke("all", new long[] { 1 }, invalid));
    }

    [Fact]
    public void Group_by_rejects_collection_keys_that_go_cannot_hash()
    {
        var collectionKey = new ExprBuiltinPredicateContext((_, _, _) => new[] { 1, 2 });

        Assert.Throws<ExprRuntimeException>(() => Invoke("groupBy", new long[] { 1 }, collectionKey));
    }

    private object? Invoke(string name, object? collection, ExprBuiltinPredicateContext context) =>
        library.InvokePredicate(name, collection, context).Value;

    private static object?[] Items(object? value) => Assert.IsAssignableFrom<IExprArray>(value).ToArray();
}
#pragma warning restore CA1861
