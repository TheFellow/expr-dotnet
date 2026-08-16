using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Facade;

public sealed class ExprEngineTests
{
    [Fact]
    public void Evaluate_runs_the_complete_default_pipeline()
    {
        object? value = ExprEngine.Evaluate(
            "sum(map(1..4, # * 2))",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(20L, value);
    }

    [Fact]
    public void Compile_exposes_consistent_inspectable_artifacts()
    {
        CompiledExpression expression = ExprEngine.Compile("1 + 2");

        Assert.Same(expression.SyntaxTree, expression.SemanticModel.SyntaxTree);
        Assert.Same(expression.SyntaxTree, expression.Program.SyntaxTree);
        var optimizedRoot = Assert.IsType<IntegerNode>(expression.SyntaxTree.Root);
        Assert.Equal(3L, optimizedRoot.Value);
        Assert.Equal(
            3L,
            ExprEngine.Run(expression, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Consumer_patched_tree_can_be_compiled_through_the_facade()
    {
        SyntaxTree original = ExprEngine.Parse("1 + 2");
        var replacement = new IntegerNode(42, original.Root.Location);
        var patched = new SyntaxTree(replacement, original.Source);

        CompiledExpression expression = ExprEngine.Compile(patched);

        Assert.Same(replacement, expression.SyntaxTree.Root);
        Assert.Equal(42L, expression.Run(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Disabled_builtin_is_parsed_as_an_ordinary_call()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.DisableBuiltin("len");

        SyntaxTree tree = ExprEngine.Parse("len([1])", configuration);

        Assert.IsType<CallNode>(tree.Root);
        Assert.Throws<ExprCheckException>(() => ExprEngine.Check(tree, configuration));
    }

    [Fact]
    public void Disabled_predicate_builtin_is_parsed_as_an_ordinary_call()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.DisableBuiltin("all");

        SyntaxTree tree = ExprEngine.Parse("all([true], true)", configuration);

        Assert.IsType<CallNode>(tree.Root);
        Assert.Throws<ExprCheckException>(() => ExprEngine.Check(tree, configuration));
    }

    [Fact]
    public void Replaced_builtin_table_is_reflected_by_parser_classification()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithBuiltins([]);

        SyntaxTree tree = ExprEngine.Parse("len([1])", configuration);

        Assert.IsType<CallNode>(tree.Root);
    }

    [Fact]
    public void Host_function_can_override_builtin_syntax_and_execution()
    {
        var function = new ExprFunction(
            "len",
            [new ExprFunctionOverload([ExprTypes.Any], ExprTypes.Integer)],
            static _ => 99L);
        ExprConfiguration configuration = ExprConfiguration.Default.WithFunction(function);

        CompiledExpression expression = ExprEngine.Compile("len([1, 2, 3])", configuration);

        Assert.IsType<CallNode>(expression.SyntaxTree.Root);
        Assert.Equal(99L, expression.Run(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Environment_function_member_overrides_builtin_syntax()
    {
        var schema = new ExprEnvironmentSchemaBuilder<FunctionEnvironment>()
            .Member(
                "len",
                static environment => environment.Len,
                new FunctionTypeDescriptor([ExprTypes.Integer], ExprTypes.Integer))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        CompiledExpression expression = ExprEngine.Compile("len(41)", configuration);

        Assert.IsType<CallNode>(expression.SyntaxTree.Root);
        Assert.Equal(
            42L,
            expression.Run(
                new FunctionEnvironment(static value => value + 1),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Configuration_node_limit_is_applied_during_parsing()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithMaximumNodeCount(2);

        SyntaxException exception = Assert.Throws<SyntaxException>(() =>
            ExprEngine.Parse("1 + 2", configuration));

        Assert.Contains("maximum allowed nodes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_apis_return_structured_diagnostics()
    {
        Assert.False(ExprEngine.TryParse("1 +", out _, out SyntaxDiagnostic? syntaxDiagnostic));
        Assert.NotNull(syntaxDiagnostic);

        SyntaxTree tree = ExprEngine.Parse("missing + 1");
        Assert.False(ExprEngine.TryCheck(tree, out _, out ExprCheckDiagnostic? checkDiagnostic));
        Assert.NotNull(checkDiagnostic);
    }

    [Fact]
    public async Task Compiled_expression_can_run_concurrently()
    {
        CompiledExpression expression = ExprEngine.Compile("sum(1..100)");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<object?>[] evaluations = [.. Enumerable.Range(0, 32).Select(_ => Task.Run(() => expression.Run(cancellationToken: cancellationToken), cancellationToken))];

        object?[] values = await Task.WhenAll(evaluations);

        Assert.All(values, static value => Assert.Equal(5050L, value));
    }

    [Fact]
    public void Compilation_profiling_option_flows_to_detailed_evaluation_default()
    {
        CompiledExpression expression = ExprEngine.Compile(
            "1 + 2",
            compilationOptions: new ExprCompilationOptions { EnableProfiling = true });

        ExprEvaluationResult result = expression.RunDetailed(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3L, result.Value);
        Assert.NotEmpty(result.Profile);
    }

    [Fact]
    public void Configuration_memory_budget_flows_to_evaluation_default()
    {
        CompiledExpression expression = ExprEngine.Compile(
            "1..100",
            ExprConfiguration.Default.WithMemoryBudget(1));

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("memory budget", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_honors_cancellation()
    {
        CompiledExpression expression = ExprEngine.Compile("sum(1..100)");
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => expression.Run(cancellationToken: source.Token));
    }

    private sealed record FunctionEnvironment(Func<long, long> Len);
}
