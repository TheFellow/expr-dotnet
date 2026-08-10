using System;
using System.Collections.Generic;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Facade;

public sealed class ReadmeExamplesTests
{
    [Fact]
    public void One_off_expression_evaluates()
    {
        object? result = ExprEngine.Evaluate(
            "all([2, 3, 5], # > 0)",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void Typed_environment_compiles_once_and_runs()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<OrderContext>()
            .Member("customer", static value => value.Customer, ExprTypes.String)
            .ArrayMember("prices", static value => value.Prices, ExprTypes.Float)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithExpectedType(ExprTypes.Boolean);
        CompiledExpression expression = ExprEngine.Compile(
            "customer == 'Ada' && sum(prices) >= 100.0",
            configuration);

        bool accepted = Assert.IsType<bool>(expression.Run(
            new OrderContext("Ada", [45.0, 60.0]),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(accepted);
    }

    [Fact]
    public void Application_function_is_statically_declared()
    {
        var isPreferred = new ExprFunction(
            "isPreferred",
            [new ExprFunctionOverload([ExprTypes.String], ExprTypes.Boolean)],
            static arguments => ((string)arguments[0]!).StartsWith("vip-", StringComparison.Ordinal));
        ExprConfiguration configuration = ExprConfiguration.Default.WithFunction(isPreferred);

        CompiledExpression expression = ExprEngine.Compile("isPreferred('vip-123')", configuration);

        Assert.True(Assert.IsType<bool>(expression.Run(
            cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void Syntax_tree_can_be_walked_printed_and_rewritten()
    {
        SyntaxTree tree = ExprEngine.Parse("price * quantity");
        SyntaxNode rewritten = new RenamePrice().Visit(tree.Root);

        Assert.NotEmpty(SyntaxWalker.Traverse(tree.Root));
        Assert.Equal("unitPrice * quantity", SyntaxPrinter.Print(rewritten));
    }

    [Fact]
    public void Evaluation_accepts_independent_limits_and_cancellation()
    {
        CompiledExpression expression = ExprEngine.Compile("40 + 2");
        var options = new ExprEvaluationOptions
        {
            WorkBudget = 100_000,
            MemoryBudget = 1_000_000,
            MaximumCollectionLength = 10_000,
            RegularExpressionTimeout = TimeSpan.FromMilliseconds(100),
        };

        object? result = expression.Run(
            options: options,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42L, result);
    }

    private sealed class RenamePrice : SyntaxRewriter
    {
        protected override SyntaxNode VisitNode(SyntaxNode node) =>
            node is IdentifierNode { Name: "price" } identifier
                ? identifier with { Name = "unitPrice" }
                : node;
    }

    private sealed record OrderContext(string Customer, IReadOnlyList<double> Prices);
}
