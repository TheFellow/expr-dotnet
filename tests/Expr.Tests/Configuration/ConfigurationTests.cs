using System;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Configuration;

public sealed class ConfigurationTests
{
    [Fact]
    public void Configuration_is_copy_on_write_and_preserves_defaults()
    {
        var function = new ExprFunction(
            "identity",
            [new ExprFunctionOverload([ExprTypes.Any], ExprTypes.Any)],
            static arguments => arguments[0]);

        ExprConfiguration configured = ExprConfiguration.Default
            .WithFunction(function)
            .WithOptimization(false)
            .WithShortCircuit(false)
            .WithMaximumNodeCount(123)
            .WithMemoryBudget(456);

        Assert.Empty(ExprConfiguration.Default.Functions);
        Assert.True(ExprConfiguration.Default.Optimize);
        Assert.True(ExprConfiguration.Default.ShortCircuit);
        Assert.Equal(ExprConfiguration.DefaultMaximumNodeCount, ExprConfiguration.Default.MaximumNodeCount);
        Assert.Same(function, configured.Functions["identity"]);
        Assert.False(configured.Optimize);
        Assert.False(configured.ShortCircuit);
        Assert.Equal(123, configured.MaximumNodeCount);
        Assert.Equal(456UL, configured.MemoryBudget);
    }

    [Fact]
    public void Constant_function_must_be_registered()
    {
        Assert.Throws<ArgumentException>(() => ExprConfiguration.Default.WithConstantFunction("missing"));

        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<NonFunctionEnvironment>()
            .Member("value", static environment => environment.Value)
            .Build();
        Assert.Throws<ArgumentException>(() =>
            ExprConfiguration.Default.WithEnvironment(schema).WithConstantFunction("value"));
    }

    [Fact]
    public void Builtin_enable_disable_operations_do_not_mutate_earlier_configuration()
    {
        var function = new ExprFunction(
            "custom",
            [new ExprFunctionOverload([], ExprTypes.Integer)],
            static _ => 42);
        ExprConfiguration registered = ExprConfiguration.Default.WithBuiltin(function);
        ExprConfiguration disabled = registered.DisableBuiltin("custom");

        Assert.DoesNotContain("custom", registered.DisabledBuiltins);
        Assert.Contains("custom", disabled.DisabledBuiltins);
        Assert.DoesNotContain("custom", disabled.EnableBuiltin("custom").DisabledBuiltins);
    }

    [Fact]
    public void Default_configuration_contains_the_complete_standard_library()
    {
        Assert.Contains("len", ExprConfiguration.Default.Builtins.Keys);
        Assert.Contains("reduce", ExprConfiguration.Default.Builtins.Keys);
        Assert.Contains("bitushr", ExprConfiguration.Default.Builtins.Keys);
    }

    [Fact]
    public void Time_zone_can_be_configured_by_portable_identifier()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithTimeZone("UTC");

        Assert.Single(configuration.Patchers);
    }

    private readonly record struct NonFunctionEnvironment(int Value);
}
