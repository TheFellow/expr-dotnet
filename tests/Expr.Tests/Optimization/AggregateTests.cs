using System.Linq;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Optimization;

// Provenance: inspiration/expr/optimizer/sum_range_test.go,
// sum_array_test.go, sum_map_test.go, count_any_test.go, and count_threshold_test.go.
public sealed class AggregateTests : OptimizerTestBase
{
    [Theory]
    [InlineData("sum(1..10)", 55)]
    [InlineData("sum(5..10)", 45)]
    [InlineData("sum(0..0)", 0)]
    [InlineData("sum(1..10, #)", 55)]
    [InlineData("sum(1..10, # * 2)", 110)]
    [InlineData("sum(1..10, 2 * #)", 110)]
    [InlineData("sum(1..10, # + 1)", 65)]
    [InlineData("sum(1..10, 10 - #)", 45)]
    [InlineData("reduce(1..10, # + #acc)", 55)]
    [InlineData("reduce(1..10, #acc + #, 10)", 65)]
    public void Constant_ranges_use_arithmetic_series(string expression, long expected)
    {
        var result = Assert.IsType<IntegerNode>(Optimize(expression).SyntaxTree.Root);

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("sum(10..1)")]
    [InlineData("reduce(10..1, # + #acc)")]
    [InlineData("sum(1..10, # / 2)")]
    public void Unsupported_or_reversed_ranges_remain_runtime_operations(string expression)
    {
        Assert.IsType<BuiltinNode>(Optimize(expression).SyntaxTree.Root);
    }

    [Fact]
    public void Sum_array_becomes_right_associative_addition()
    {
        var result = Assert.IsType<BinaryNode>(Optimize("sum([a,b,c])").SyntaxTree.Root);

        Assert.Equal("a", Assert.IsType<IdentifierNode>(result.Left).Name);
        var right = Assert.IsType<BinaryNode>(result.Right);
        Assert.Equal("b", Assert.IsType<IdentifierNode>(right.Left).Name);
        Assert.Equal("c", Assert.IsType<IdentifierNode>(right.Right).Name);
    }

    [Fact]
    public void Sum_map_becomes_predicate_sum()
    {
        var result = Assert.IsType<BuiltinNode>(Optimize("sum(map(users, .Age))").SyntaxTree.Root);

        Assert.Equal("sum", result.Name);
        Assert.Equal(2, result.Arguments.Count);
        Assert.IsType<IdentifierNode>(result.Arguments[0]);
        Assert.IsType<PredicateNode>(result.Arguments[1]);
    }

    [Fact]
    public void Sum_array_rewrite_has_an_explicit_generated_depth_bound()
    {
        string expression = $"sum([{string.Join(',', Enumerable.Repeat("value", 257))}])";

        var result = Assert.IsType<BuiltinNode>(Optimize(expression).SyntaxTree.Root);

        Assert.Equal("sum", result.Name);
    }

    [Theory]
    [InlineData("count(items, .active) > 0")]
    [InlineData("count(items, .active) >= 1")]
    public void Count_existence_comparison_becomes_any(string expression)
    {
        var result = Assert.IsType<BuiltinNode>(Optimize(expression).SyntaxTree.Root);

        Assert.Equal("any", result.Name);
    }

    [Theory]
    [InlineData("count(items, .active) > 100", 101)]
    [InlineData("count(items, .active) >= 50", 50)]
    [InlineData("count(items, .active) < 100", 100)]
    [InlineData("count(items, .active) <= 50", 51)]
    public void Count_comparison_sets_early_exit_threshold(string expression, int expected)
    {
        var binary = Assert.IsType<BinaryNode>(Optimize(expression).SyntaxTree.Root);
        var count = Assert.IsType<BuiltinNode>(binary.Left);

        Assert.Equal(expected, count.Threshold);
    }

    [Theory]
    [InlineData("count(items, .active) < 1")]
    [InlineData("count(items, .active) <= 0")]
    [InlineData("count(items, .active) == 10")]
    [InlineData("count(items, .active) > -1")]
    public void Unsafe_or_unprofitable_count_thresholds_are_not_added(string expression)
    {
        var binary = Assert.IsType<BinaryNode>(Optimize(expression).SyntaxTree.Root);
        var count = Assert.IsType<BuiltinNode>(binary.Left);

        Assert.Null(count.Threshold);
    }
}
