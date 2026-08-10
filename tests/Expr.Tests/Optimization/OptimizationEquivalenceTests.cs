using System.Collections.Generic;
using Expr.Checking;
using Expr.Configuration;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Optimization;

// Provenance: inspiration/expr/optimizer/optimizer_test.go TestOptimize compares
// optimized and unoptimized programs across the representative optimizer corpus.
public sealed class OptimizationEquivalenceTests : OptimizerTestBase
{
    public static IEnumerable<object[]> RepresentativeExpressions()
    {
        yield return ["1 + 2"];
        yield return ["sum([a, b, c])"];
        yield return ["sum(1..10, # * 1000)"];
        yield return ["all(1..3, # > 0) && all(1..3, # < 4)"];
        yield return ["none(1..3, # == 0) && none(1..3, # == 4)"];
        yield return ["any(1..3, # == 1) || any(1..3, # == 2)"];
        yield return ["len(filter(users, true))"];
        yield return ["first(map(filter(users, true), .Age))"];
        yield return ["count(items, .active) > 100"];
    }

    [Theory]
    [MemberData(nameof(RepresentativeExpressions))]
    public void Optimization_preserves_the_checked_result_type(string expression)
    {
        ExprConfiguration enabled = ExprConfiguration.Default.AllowUndefinedVariables();
        ExprConfiguration disabled = enabled.WithOptimization(false);
        SyntaxTree tree = new SyntaxParser().Parse(expression);
        ExprSemanticModel original = new ExprChecker().Check(tree, disabled);

        ExprSemanticModel optimized = Optimize(expression, enabled);

        Assert.True(original.ResultType.IsEquivalentTo(optimized.ResultType));
    }
}
