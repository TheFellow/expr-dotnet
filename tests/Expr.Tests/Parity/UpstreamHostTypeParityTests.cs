using System;
using Expr.Checking;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Parity;

/// <summary>Covers nominal and promoted host-type regressions from the pinned Go suite.</summary>
public sealed class UpstreamHostTypeParityTests
{
    [Fact]
    public void Issue105_explicit_schema_promotes_unambiguous_nested_members()
    {
        var environment = new PromotedEnvironment(
            new CompositeFields(new TextField(string.Empty), new IntegerField(0)));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<PromotedEnvironment>()
            .Member("A", static value => value.C.A, new ObjectTypeDescriptor(typeof(TextField)))
            .Member("B", static value => value.C.B, new ObjectTypeDescriptor(typeof(IntegerField)))
            .Member("C", static value => value.C, new ObjectTypeDescriptor(typeof(CompositeFields)))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        object? result = ExprEngine.Evaluate(
            "A.Field == '' && C.A.Field == '' && B.Field == 0 && C.B.Field == 0",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Issue461_nominal_string_wrapper_is_not_interchangeable_with_string()
    {
        var environment = new NominalEnvironment(
            new EnvironmentString("string"),
            "string",
            new NominalFields(new EnvironmentString("string"), "string"));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<NominalEnvironment>()
            .Member("S", static value => value.S, new ObjectTypeDescriptor(typeof(EnvironmentString)))
            .Member("Str", static value => value.Str, ExprTypes.String)
            .Member("EnvField", static value => value.EnvField, new ObjectTypeDescriptor(typeof(NominalFields)))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        string[] validExpressions =
        [
            "Str == Str",
            "S == S",
            "Str == 'string'",
            "EnvField.Str == EnvField.Str",
            "EnvField.S == EnvField.S",
            "EnvField.Str == 'string'",
        ];
        foreach (string expression in validExpressions)
        {
            Assert.Equal(true, ExprEngine.Evaluate(
                expression,
                environment,
                configuration,
                cancellationToken: TestContext.Current.CancellationToken));
        }

        string[] invalidExpressions =
        [
            "Str == S",
            "S == 'string'",
            "EnvField.Str == EnvField.S",
            "EnvField.S == 'string'",
        ];
        foreach (string expression in invalidExpressions)
        {
            ExprCheckException exception = Assert.Throws<ExprCheckException>(() =>
                ExprEngine.Compile(expression, configuration));
            Assert.Contains("mismatched types", exception.Message, StringComparison.Ordinal);
        }
    }

    private sealed record TextField(string Field);

    private sealed record IntegerField(long Field);

    private sealed record CompositeFields(TextField A, IntegerField B);

    private sealed record PromotedEnvironment(CompositeFields C);

    private readonly record struct EnvironmentString(string Value);

    private sealed record NominalFields(EnvironmentString S, string Str);

    private sealed record NominalEnvironment(EnvironmentString S, string Str, NominalFields EnvField);
}
