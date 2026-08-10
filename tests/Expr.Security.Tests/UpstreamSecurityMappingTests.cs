using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Security.Tests;

/// <summary>Documents intentional security mappings for upstream dynamic-reflection behavior.</summary>
public sealed class UpstreamSecurityMappingTests
{
    [Fact]
    public void Issue934_any_typed_values_cannot_discover_public_or_nonpublic_members()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<AnyEnvironment>()
            .Member("value", static environment => environment.Value, ExprTypes.Any)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
        var environment = new AnyEnvironment(new VisibilityPayload());

        CompiledExpression expression = ExprEngine.Compile("value.Allowed", configuration);
        ExprExecutionException runtimeFailure = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(environment, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("cannot fetch Allowed", runtimeFailure.Message, System.StringComparison.Ordinal);

        foreach (string member in new[] { "Allowed", "Hidden" })
        {
            CompiledExpression conditional = ExprEngine.Compile(
                $"(true ? value : 'string').{member}",
                configuration);
            ExprExecutionException conditionalFailure = Assert.Throws<ExprExecutionException>(() =>
                conditional.Run(environment, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains($"cannot fetch {member}", conditionalFailure.Message, System.StringComparison.Ordinal);
        }
    }

    private sealed record AnyEnvironment(object Value);

    private sealed class VisibilityPayload
    {
        public string Allowed => "allowed";

        private string Hidden => "hidden";
    }
}
