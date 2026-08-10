using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Expr.Configuration;
using Expr.Runtime;
using Xunit;

namespace Expr.Tests.Parity;

public sealed class MapDefaultValueParityTests
{
    [Fact]
    [RequiresUnreferencedCode("Infers nested map types from the exact upstream-shaped runtime environment.")]
    public void TestExpr_map_default_values()
    {
        var environment = new Dictionary<string, object?>
        {
            ["foo"] = new Dictionary<string, string>(),
            ["bar"] = new Dictionary<string, object?>(),
        };
        ExprEnvironmentSchema schema = ExprEnvironmentSchema.FromDictionary(environment);
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        object? result = ExprEngine.Evaluate(
            "foo['missing'] == '' && bar['missing'] == nil",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(true, result);
        Assert.Null(ExprEngine.Evaluate(
            "get(foo, 'missing')",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    [RequiresDynamicCode("Reflects the named CLR map environment methods used by the upstream compile-check case.")]
    [RequiresUnreferencedCode("Reflects the named CLR map environment methods used by the upstream compile-check case.")]
    public void TestExpr_map_default_values_compile_check()
    {
        _ = new MapStringStringEnvironment();
        _ = new MapStringIntegerEnvironment();
        ExprConfiguration stringConfiguration = ExprConfiguration.Default
            .WithEnvironment(ExprEnvironmentSchema.Reflect<MapStringStringEnvironment>())
            .AllowUndefinedVariables();
        ExprConfiguration integerConfiguration = ExprConfiguration.Default
            .WithEnvironment(ExprEnvironmentSchema.Reflect<MapStringIntegerEnvironment>())
            .AllowUndefinedVariables();

        _ = ExprEngine.Compile("Split(foo, sep)", stringConfiguration);
        _ = ExprEngine.Compile("foo / bar", integerConfiguration);
    }

    private sealed class MapStringStringEnvironment : Dictionary<string, string>
    {
        public string[] Split(string value, string separator) =>
            value.Split(separator, System.StringSplitOptions.None);
    }

    private sealed class MapStringIntegerEnvironment : Dictionary<string, int>
    {
    }
}
