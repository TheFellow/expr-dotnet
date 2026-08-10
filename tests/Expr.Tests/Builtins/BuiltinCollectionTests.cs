using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Builtins;
using Expr.Runtime;
using Xunit;

// Inline arrays make the collection input and expected value legible together in these non-hot-path tests.
#pragma warning disable CA1861

namespace Expr.Tests.Builtins;

public sealed class BuiltinCollectionTests
{
    private readonly ExprBuiltinLibrary library = new();

    [Fact]
    public void Aggregates_flatten_nested_numeric_arrays()
    {
        object?[] values = [1L, new[] { 2L, 3L }, new object?[] { 4D, new[] { 5 } }];

        Assert.Equal(5, Invoke("max", values));
        Assert.Equal(1L, Invoke("min", values));
        Assert.Equal(3D, Invoke("mean", values));
        Assert.Equal(3D, Invoke("median", values));
        Assert.Equal(0D, Invoke("mean", Array.Empty<long>()));
        Assert.Equal(0D, Invoke("median", Array.Empty<long>()));
    }

    [Fact]
    public void Aggregate_rejects_non_numeric_nested_values()
    {
        Assert.Throws<ExprRuntimeException>(() => Invoke("max", new object?[] { 1L, "2" }));
        Assert.Throws<ExprRuntimeException>(() => Invoke("mean", new object?[] { 1L, true }));
    }

    [Fact]
    public void Element_and_take_functions_cover_empty_negative_and_clamped_indexes()
    {
        long[] values = [1, 2, 3];

        Assert.Equal(1L, Invoke("first", values));
        Assert.Equal(3L, Invoke("last", values));
        Assert.Null(Invoke("first", Array.Empty<long>()));
        Assert.Equal(3L, Invoke("get", values, -1L));
        Assert.Null(Invoke("get", values, 99L));
        Assert.Equal((byte)0xA9, Invoke("get", "é", -1L));
        Assert.Equal([1L, 2L], Values(Invoke("take", values, 2L)));
        Assert.Equal([1L, 2L, 3L], Values(Invoke("take", values, 99L)));
        Assert.Throws<ExprRuntimeException>(() => Invoke("take", values, -1L));
    }

    [Fact]
    public void Get_honors_expr_member_aliases_and_hidden_members()
    {
        var value = new MemberFixture();

        Assert.Equal(42L, Invoke("get", value, "answer"));
        Assert.Null(Invoke("get", value, nameof(MemberFixture.Hidden)));
        Assert.Null(Invoke("get", value, "missing"));
    }

    [Fact]
    public void Get_returns_a_bound_cached_delegate_for_unambiguous_public_methods()
    {
        var value = new MemberFixture();

        var callable = Assert.IsAssignableFrom<Delegate>(Invoke("get", value, nameof(MemberFixture.Add)));

        Assert.Equal(45L, callable.DynamicInvoke(3L));
    }

    [Fact]
    public void Get_does_not_expose_clr_collection_properties_as_expr_members()
    {
        Assert.Throws<ExprRuntimeException>(() => Invoke("get", new[] { 1, 2 }, "Length"));
        Assert.Null(Invoke("get", new Dictionary<string, int>(), "Count"));
    }

    [Fact]
    public void Map_transforms_round_trip_and_duplicate_pairs_use_last_value()
    {
        var source = new Dictionary<string, object?> { ["foo"] = 1L, ["bar"] = 2L };
        object? pairs = Invoke("toPairs", source);
        IExprMap roundTrip = Assert.IsAssignableFrom<IExprMap>(Invoke("fromPairs", pairs));

        Assert.Equal(2, roundTrip.Count);
        Assert.True(roundTrip.TryGetValue("foo", out object? foo));
        Assert.Equal(1L, foo);
        var duplicates = new object?[] { new object?[] { "a", 1L }, new object?[] { "a", 2L } };
        IExprMap lastWins = Assert.IsAssignableFrom<IExprMap>(Invoke("fromPairs", (object?)duplicates));
        Assert.True(lastWins.TryGetValue("a", out object? last));
        Assert.Equal(2L, last);
        Assert.Equal(["foo", "bar"], Values(Invoke("keys", source)));
        Assert.Equal([1L, 2L], Values(Invoke("values", source)));
    }

    [Fact]
    public void Collection_transforms_are_immutable_and_resource_charged()
    {
        int[] source = [1, 2, 3];

        Assert.Equal([3, 2, 1], Values(Invoke("reverse", source)));
        Assert.Equal([1L, 2L, 3L], Values(Invoke("uniq", new long[] { 1, 2, 1, 3, 2 })));
        Assert.Equal([1, 2, 3, 4], Values(Invoke("concat", new[] { 1, 2 }, new[] { 3, 4 })));
        Assert.Equal([1, 2, 3, 4], Values(Invoke("flatten", (object?)new object?[] { 1, new object?[] { 2, new[] { 3, 4 } } })));
        Assert.Equal([1, 2, 3], Values(Invoke("sort", new[] { 3, 1, 2 })));
        Assert.Equal([3, 2, 1], Values(Invoke("sort", new[] { 3, 1, 2 }, "desc")));
        Assert.Equal([1, 2, 3], source);
        Assert.Equal(4UL, library.Get("concat").Invoke([new[] { 1, 2 }, new[] { 3, 4 }]).MemoryCost);
    }

    [Fact]
    public void Sort_treats_nan_as_unordered_like_go_less()
    {
        object? result = Invoke("sort", new[] { double.NaN, 2D, 1D });

        object?[] values = Values(result);
        Assert.Contains(double.NaN, values);
        Assert.Contains(1D, values);
        Assert.Contains(2D, values);
    }

    [Fact]
    public void Group_by_keeps_nan_keys_distinct_and_charges_every_group_value()
    {
        ExprInvocationResult result = library.InvokePredicate(
            "groupBy",
            new[] { 1D, 2D },
            new ExprBuiltinPredicateContext((_, _, _) => double.NaN));

        IExprMap groups = Assert.IsAssignableFrom<IExprMap>(result.Value);
        Assert.Equal(2, groups.Count);
        Assert.Equal(2UL, result.MemoryCost);

        var bounded = new ExprBuiltinLibrary(new ExprBuiltinOptions { MaximumAllocation = 1 });
        Assert.Throws<ExprRuntimeException>(() => bounded.InvokePredicate(
            "groupBy",
            new[] { 1D, 2D },
            new ExprBuiltinPredicateContext((item, _, _) => item)));
    }

    [Fact]
    public void Flatten_and_aggregates_stop_at_the_configured_depth()
    {
        var options = new ExprBuiltinOptions { MaximumDepth = 3 };
        var bounded = new ExprBuiltinLibrary(options);
        var cycle = new object?[1];
        cycle[0] = cycle;

        Assert.Throws<ExprRuntimeException>(() => bounded.Get("flatten").Invoke([cycle]));
        Assert.Throws<ExprRuntimeException>(() => bounded.Get("max").Invoke([cycle]));
    }

    [Fact]
    public void Collection_allocation_limit_is_applied_before_materialization()
    {
        var bounded = new ExprBuiltinLibrary(new ExprBuiltinOptions { MaximumAllocation = 3 });

        Assert.Throws<ExprRuntimeException>(() => bounded.Get("concat").Invoke([new[] { 1, 2 }, new[] { 3, 4 }]));
        Assert.Throws<ExprRuntimeException>(() => bounded.Get("reverse").Invoke([new[] { 1, 2, 3, 4 }]));
    }

    [Fact]
    public void From_pairs_rejects_collection_keys_that_go_cannot_hash()
    {
        object?[] pairs = [new object?[] { new[] { 1, 2 }, "value" }];

        Assert.Throws<ExprRuntimeException>(() => Invoke("fromPairs", (object?)pairs));
    }

    private object? Invoke(string name, params object?[] arguments) => library.Get(name).Invoke(arguments).Value;

    private static object?[] Values(object? value) => Assert.IsAssignableFrom<IExprArray>(value).ToArray();

    private sealed class MemberFixture
    {
        [ExprMember("answer")]
        public long Value => 42;

        [ExprMember(Ignore = true)]
        public string Hidden => "secret";

        public long Add(long value) => Value + value;
    }
}
#pragma warning restore CA1861
