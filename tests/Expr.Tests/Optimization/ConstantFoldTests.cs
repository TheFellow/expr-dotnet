using System;
using Expr.Checking;
using Expr.Configuration;
using Expr.Optimization;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Optimization;

// Provenance: inspiration/expr/optimizer/fold_test.go and optimizer_test.go.
public sealed class ConstantFoldTests : OptimizerTestBase
{
    [Fact]
    public void Arithmetic_array_and_index_expressions_fold_to_literals()
    {
        ExprSemanticModel result = Optimize("[1,2,3][5*5-25]");

        var member = Assert.IsType<MemberNode>(result.SyntaxTree.Root);
        var array = Assert.IsType<ConstantNode>(member.Target);
        Assert.Equal([1L, 2L, 3L], Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<object?>>(array.Value));
        Assert.Equal(0, Assert.IsType<IntegerNode>(member.Property).Value);
    }

    [Fact]
    public void Mixed_numeric_expression_folds_to_float()
    {
        ExprSemanticModel result = Optimize("1 + 2.0 * ((1.0 * 2) / 2) - 0");

        Assert.Equal(3D, Assert.IsType<FloatNode>(result.SyntaxTree.Root).Value);
        Assert.Same(ExprTypes.Float, result.ResultType);
    }

    [Fact]
    public void Boolean_expression_folds_with_expr_shortcuts()
    {
        ExprSemanticModel result = Optimize(
            "(true and false) or (true or false) or (false and false) or (true and (true == false))");

        Assert.True(Assert.IsType<BooleanNode>(result.SyntaxTree.Root).Value);
    }

    [Fact]
    public void Nested_filters_are_combined_and_refolded()
    {
        ExprSemanticModel result = Optimize("filter(filter(1..2, true), true)");

        var filter = Assert.IsType<BuiltinNode>(result.SyntaxTree.Root);
        Assert.Equal("filter", filter.Name);
        Assert.True(Assert.IsType<BooleanNode>(Assert.IsType<PredicateNode>(filter.Arguments[1]).Body).Value);
    }

    [Fact]
    public void Integer_remainder_by_zero_is_reported_at_the_operator()
    {
        ExprOptimizationException exception = Assert.Throws<ExprOptimizationException>(() => Optimize("10 % 0"));

        Assert.Equal("integer divide by zero", exception.Message);
        Assert.Equal(3, exception.Location.Start);
    }

    [Fact]
    public void Disabled_optimization_returns_the_exact_checked_model()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithOptimization(false);
        SyntaxTree tree = new SyntaxParser().Parse("1 + 2");
        ExprSemanticModel checkedModel = new Expr.Checking.ExprChecker().Check(tree, configuration);

        ExprSemanticModel result = ExprOptimizer.Optimize(checkedModel, configuration);

        Assert.Same(checkedModel, result);
        Assert.IsType<BinaryNode>(result.SyntaxTree.Root);
    }

    [Fact]
    public void Enabled_optimization_preserves_identity_when_no_pass_applies()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.AllowUndefinedVariables();
        SyntaxTree tree = new SyntaxParser().Parse("left + right");
        ExprSemanticModel checkedModel = new ExprChecker().Check(tree, configuration);

        ExprSemanticModel result = ExprOptimizer.Optimize(checkedModel, configuration);

        Assert.Same(checkedModel, result);
        Assert.Same(tree.Root, result.SyntaxTree.Root);
    }

    [Fact]
    public void Constant_functions_fold_after_their_arguments_and_preserve_location()
    {
        var upper = new ExprFunction(
            "upperCustom",
            [new ExprFunctionOverload([ExprTypes.String], ExprTypes.String)],
            static arguments => ((string)arguments[0]!).ToUpperInvariant());
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithFunction(upper)
            .WithConstantFunction("upperCustom");

        ExprSemanticModel result = Optimize("upperCustom('he' + 'llo')", configuration);

        var constant = Assert.IsType<ConstantNode>(result.SyntaxTree.Root);
        Assert.Equal("HELLO", constant.Value);
        Assert.Equal(0, constant.Location.Start);
    }

    [Fact]
    public void Constant_function_failures_are_wrapped_with_source_location()
    {
        var fail = new ExprFunction(
            "fail",
            [new ExprFunctionOverload([], ExprTypes.Integer)],
            static _ => throw new InvalidOperationException("deliberate"));
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithFunction(fail)
            .WithConstantFunction("fail");

        ExprOptimizationException exception = Assert.Throws<ExprOptimizationException>(() => Optimize("fail()", configuration));

        Assert.Equal("deliberate", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void Safe_constant_function_charges_respect_the_memory_budget()
    {
        var allocate = new ExprFunction(
            "allocate",
            [new ExprFunctionOverload([], ExprTypes.String)],
            safeInvoker: static _ => new ExprInvocationResult("value", 10));
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithFunction(allocate)
            .WithConstantFunction("allocate")
            .WithMemoryBudget(5);

        ExprOptimizationException exception = Assert.Throws<ExprOptimizationException>(() =>
            Optimize("allocate()", configuration));

        Assert.Contains("memory budget", exception.Message, StringComparison.Ordinal);
    }
}
