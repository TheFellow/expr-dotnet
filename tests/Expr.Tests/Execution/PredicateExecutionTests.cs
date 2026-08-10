using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Xunit;

namespace Expr.Tests.Execution;

public sealed class PredicateExecutionTests
{
    public static TheoryData<string, object?> ScalarPredicates => new()
    {
        { "all([1, 2, 3], # > 0)", true },
        { "none([1, 2, 3], # < 0)", true },
        { "any([1, 2, 3], # == 2)", true },
        { "one([1, 2, 3], # == 2)", true },
        { "find([1, 2, 3], # > 1)", 2L },
        { "findIndex([1, 2, 3], # > 1)", 1L },
        { "findLast([1, 2, 3, 2], # == 2)", 2L },
        { "findLastIndex([1, 2, 3, 2], # == 2)", 3L },
        { "count([1, 2, 3], # > 1)", 2L },
        { "sum([1, 2, 3])", 6L },
        { "reduce([1, 2, 3], #acc + #, 0)", 6L },
    };

    [Theory]
    [MemberData(nameof(ScalarPredicates))]
    public void Predicate_builtins_match_upstream_scalar_results(string expression, object? expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Fact]
    public void Filter_and_map_collect_predicate_values()
    {
        AssertArray([2L, 3L], Evaluate("filter([1, 2, 3], # > 1)"));
        AssertArray([2L, 4L, 6L], Evaluate("map([1, 2, 3], # * 2)"));
    }

    [Fact]
    public void GroupBy_and_sortBy_preserve_upstream_collection_shapes()
    {
        var grouped = Assert.IsAssignableFrom<IExprMap>(
            Evaluate("groupBy([1, 2, 3, 4], # % 2 == 0 ? 'even' : 'odd')"));
        Assert.True(grouped.TryGetValue("even", out object? evens));
        Assert.True(grouped.TryGetValue("odd", out object? odds));
        AssertArray([2L, 4L], evens);
        AssertArray([1L, 3L], odds);

        AssertArray(
            [3L, 2L, 1L],
            Evaluate("sortBy([1, 3, 2], #, 'desc')"));
    }

    [Fact]
    public void Nested_predicates_keep_isolated_scope_state()
    {
        AssertArray(
            [2L, 3L],
            Evaluate("filter([1, 2, 3], any([#], # > 1))"));
    }

    [Fact]
    public void Predicate_builtins_only_use_map_length_when_item_is_not_read()
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["first"] = 1L,
            ["second"] = 2L,
        };
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(
            ExprEnvironmentSchema.FromDictionary(environment));

        object? result = ExprEngine.Evaluate(
            "all($env, true)",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(true, result);

        Assert.Throws<ExprExecutionException>(() => ExprEngine.Evaluate(
            "all($env, # != nil)",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SortBy_keeps_unordered_nan_keys_stable()
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["values"] = new[] { double.NaN, 1D, double.NaN },
        };
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithEnvironment(ExprEnvironmentSchema.FromDictionary(environment));

        object? result = ExprEvaluator.Shared.Evaluate(
            ExecutionTestCompiler.Compile("sortBy(values, #)", configuration),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertArray([double.NaN, 1D, double.NaN], result);
    }

    [Fact]
    public void GroupBy_keeps_nan_keys_distinct_like_go_maps()
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["values"] = new[] { 1D, 2D },
            ["nan"] = double.NaN,
        };
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithEnvironment(ExprEnvironmentSchema.FromDictionary(environment));

        object? result = ExprEvaluator.Shared.Evaluate(
            ExecutionTestCompiler.Compile("groupBy(values, nan)", configuration),
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

        var groups = Assert.IsAssignableFrom<IExprMap>(result);
        Assert.Equal(2, groups.Count);
    }

    private static object? Evaluate(string source) =>
        ExprEvaluator.Shared.Evaluate(
            ExecutionTestCompiler.Compile(source),
            cancellationToken: TestContext.Current.CancellationToken);

    private static void AssertArray(IReadOnlyList<object?> expected, object? actual)
    {
        var array = Assert.IsAssignableFrom<IExprArray>(actual);
        Assert.Equal(expected, array.ToArray());
    }
}
