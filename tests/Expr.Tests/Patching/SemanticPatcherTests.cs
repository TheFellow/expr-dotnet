using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Expr.Checking;
using Expr.Configuration;
using Expr.Patching;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Patching;

public sealed class SemanticPatcherTests
{
    [Fact]
    [RequiresUnreferencedCode("ExprChecker exposes reflection-backed CLR member checking.")]
    public void Operator_override_replaces_an_otherwise_invalid_operation()
    {
        _ = new Environment(new Box(string.Empty), new Box(string.Empty));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<Environment>()
            .Member("left", static environment => environment.Left)
            .Member("right", static environment => environment.Right)
            .Build();
        var concat = new ExprFunction(
            "concatBoxes",
            [new ExprFunctionOverload(
                [new ObjectTypeDescriptor(typeof(Box)), new ObjectTypeDescriptor(typeof(Box))],
                ExprTypes.String)],
            static _ => "joined");
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithFunction(concat)
            .WithOperator("+", "concatBoxes");

        ExprSemanticModel model = Check("left + right", configuration);

        var call = Assert.IsType<CallNode>(model.SyntaxTree.Root);
        Assert.Equal("concatBoxes", Assert.IsType<IdentifierNode>(call.Callee).Name);
        Assert.Same(ExprTypes.String, model.ResultType);
    }

    [Fact]
    [RequiresUnreferencedCode("ExprChecker exposes reflection-backed CLR member checking.")]
    public void Context_patcher_prepends_cancellation_token_once()
    {
        _ = new ContextEnvironment(CancellationToken.None);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ContextEnvironment>()
            .Member("ctx", static environment => environment.Context)
            .Build();
        var function = new ExprFunction(
            "work",
            [new ExprFunctionOverload(
                [ExprTypes.Integer, new ObjectTypeDescriptor(typeof(CancellationToken))],
                ExprTypes.Integer)],
            static arguments => arguments[0]);
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithFunction(function)
            .WithContext("ctx");

        ExprSemanticModel model = Check("work(42)", configuration);

        var call = Assert.IsType<CallNode>(model.SyntaxTree.Root);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal("ctx", Assert.IsType<IdentifierNode>(call.Arguments[1]).Name);
        Assert.Same(ExprTypes.Integer, model.ResultType);
    }

    [Fact]
    [RequiresUnreferencedCode("Exercises reflection-backed environment method checking.")]
    public void Context_patcher_supports_environment_instance_methods()
    {
        _ = new ContextMethodEnvironment();
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ContextMethodEnvironment>()
            .Member("ctx", static environment => environment.Context)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithContext("ctx");

        ExprSemanticModel model = Check("Work(42)", configuration);

        var call = Assert.IsType<CallNode>(model.SyntaxTree.Root);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Same(ExprTypes.Integer, model.ResultType);
    }

    [Fact]
    [RequiresUnreferencedCode("ExprChecker exposes reflection-backed CLR member checking.")]
    public void Time_zone_patcher_adds_constant_to_date_and_now()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithTimeZone(TimeZoneInfo.Utc);

        ExprSemanticModel model = Check("now()", configuration);

        var builtin = Assert.IsType<BuiltinNode>(model.SyntaxTree.Root);
        var constant = Assert.IsType<ConstantNode>(Assert.Single(builtin.Arguments));
        Assert.Same(TimeZoneInfo.Utc, constant.Value);
        Assert.Same(ExprTypes.Time, model.ResultType);
    }

    [Fact]
    [RequiresUnreferencedCode("Exercises reflection-backed value-provider metadata discovery.")]
    public void Typed_value_provider_converts_semantics_without_mutating_the_ast()
    {
        _ = new ValueEnvironment(new IntegerValue(41));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ValueEnvironment>()
            .Member("value", static environment => environment.Value)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithValueProviders();

        ExprSemanticModel model = Check("value + 1", configuration);

        var binary = Assert.IsType<BinaryNode>(model.SyntaxTree.Root);
        var identifier = Assert.IsType<IdentifierNode>(binary.Left);
        Assert.True(model.TryGetSemantics(identifier, out ExprNodeSemantics? semantics));
        Assert.NotNull(semantics?.ValueConversion);
        Assert.Same(ExprTypes.Integer, semantics.ValueConversion.ValueType);
        Assert.Same(ExprTypes.Integer, model.ResultType);
    }

    [Fact]
    public void Invalid_operator_configuration_fails_before_tree_processing()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithOperator("+", "missing");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            Check("1 + 2", configuration));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [RequiresUnreferencedCode("ExprChecker exposes reflection-backed CLR member checking.")]
    private static ExprSemanticModel Check(string expression, ExprConfiguration configuration) =>
        new ExprChecker().Check(new SyntaxParser().Parse(expression), configuration);

    private sealed record Environment(Box Left, Box Right);

    private sealed record ContextEnvironment(CancellationToken Context);

    private sealed record ValueEnvironment(IntegerValue Value);

    private sealed record IntegerValue(long Value) : IExprValueProvider<long>
    {
        public long ToExprValue() => Value;
    }

    private sealed class ContextMethodEnvironment
    {
        public CancellationToken Context => CancellationToken.None;

        public long Work(long value, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? 0 : value;
    }

    private sealed record Box(string Value);
}
