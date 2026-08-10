using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Optimization;

// Provenance: inspiration/expr/optimizer/optimizer_test.go TestOptimize_predicate_combination*.
public sealed class PredicateCombinationTests : OptimizerTestBase
{
    [Theory]
    [InlineData("all(users, .Age > 18) and all(users, .Age < 30)", "all", "and")]
    [InlineData("all(users, .Age > 18) && all(users, .Age < 30)", "all", "&&")]
    [InlineData("any(users, .Age > 18) or any(users, .Age < 30)", "any", "or")]
    [InlineData("any(users, .Age > 18) || any(users, .Age < 30)", "any", "||")]
    [InlineData("none(users, .Age > 18) and none(users, .Age < 30)", "none", "or")]
    [InlineData("none(users, .Age > 18) && none(users, .Age < 30)", "none", "||")]
    public void Compatible_predicates_over_same_collection_are_combined(
        string expression,
        string function,
        string operation)
    {
        var result = Assert.IsType<BuiltinNode>(Optimize(expression).SyntaxTree.Root);

        Assert.Equal(function, result.Name);
        var predicate = Assert.IsType<PredicateNode>(result.Arguments[1]);
        Assert.Equal(operation, Assert.IsType<BinaryNode>(predicate.Body).Operator);
    }

    [Fact]
    public void Different_collections_are_not_combined()
    {
        var result = Optimize("all(users, true) and all(others, true)").SyntaxTree.Root;

        Assert.IsType<BinaryNode>(result);
    }

    [Fact]
    public void Nested_compatible_predicates_are_combined_recursively()
    {
        const string expression =
            "all(users, {all(.Friends, {.Age == 18})}) && " +
            "all(users, {all(.Friends, {.Name != 'Bob'})})";

        var outer = Assert.IsType<BuiltinNode>(Optimize(expression).SyntaxTree.Root);
        var outerPredicate = Assert.IsType<PredicateNode>(outer.Arguments[1]);
        var inner = Assert.IsType<BuiltinNode>(outerPredicate.Body);
        var innerPredicate = Assert.IsType<PredicateNode>(inner.Arguments[1]);

        Assert.Equal("&&", Assert.IsType<BinaryNode>(innerPredicate.Body).Operator);
    }

    [Fact]
    public void Structurally_equal_collections_ignore_source_locations()
    {
        var result = Optimize("all((users), true) and all(users, false)").SyntaxTree.Root;

        Assert.IsType<BuiltinNode>(result);
    }
}
