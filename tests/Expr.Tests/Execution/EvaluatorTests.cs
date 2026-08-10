using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Patching;
using Expr.Runtime;
using Xunit;

namespace Expr.Tests.Execution;

public sealed class EvaluatorTests
{
    [Fact]
    public void Timezone_exposes_the_go_string_method_name()
    {
        object? result = ExprEvaluator.Shared.Evaluate(
            ExecutionTestCompiler.Compile("timezone('UTC').String()"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("UTC", result);
    }

    public static TheoryData<string, object?> CoreExpressions => new()
    {
        { "2 + 3", 5L },
        { "2.5 + 3", 5.5D },
        { "6 * 7", 42L },
        { "1 + 2 * 3 - 4 / 2", 5D },
        { "5 % 2", 1L },
        { "2 ^ 3", 8D },
        { "-5", -5L },
        { "'hello' + ' ' + 'world'", "hello world" },
        { "'hello world' startsWith 'hello'", true },
        { "'hello world' endsWith 'world'", true },
        { "'hello world' contains 'lo wo'", true },
        { "'hello123' matches '^hello\\\\d+$'", true },
        { "[1, 2, 3][1]", 2L },
        { "{'a': 1, 'b': 2}.b", 2L },
        { "len([1, 2, 3])", 3L },
        { "true ? 1 : 2", 1L },
        { "nil ?? 42", 42L },
        { "let answer = 40; answer + 2", 42L },
    };

    [Theory]
    [MemberData(nameof(CoreExpressions))]
    public void Evaluator_ports_core_vm_operations(string expression, object? expected)
    {
        object? actual = ExprEvaluator.Shared.Evaluate(
            ExecutionTestCompiler.Compile(expression),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Arrays_ranges_slices_and_maps_are_immutable_expr_collections()
    {
        var range = Assert.IsAssignableFrom<IExprArray>(Evaluate("1..5"));
        Assert.Equal([1L, 2L, 3L, 4L, 5L], range.Cast<object?>());

        var slice = Assert.IsAssignableFrom<IExprArray>(Evaluate("[1, 2, 3, 4][1:3]"));
        Assert.Equal([2L, 3L], slice.Cast<object?>());

        var map = Assert.IsAssignableFrom<IExprMap>(Evaluate("{'a': 1, 'b': 2}"));
        Assert.True(map.TryGetValue("b", out object? value));
        Assert.Equal(2L, value);
    }

    [Fact]
    public void Earlier_duplicate_map_literal_entry_wins_like_upstream()
    {
        Assert.Equal(1L, Evaluate("{'a': 1, 'a': 2}.a"));
    }

    [Fact]
    public void Byte_strings_support_fetch_slice_equality_membership_and_vm_length()
    {
        Assert.Equal((byte)'b', Evaluate("b'abc'[1]"));
        Assert.True((bool)Evaluate("b'abc' == b'abc'")!);
        Assert.True((bool)Evaluate("98 in b'abc'")!);

        var slice = Assert.IsType<ReadOnlyMemory<byte>>(Evaluate("b'abc'[1:]"));
        Assert.Equal(new byte[] { (byte)'b', (byte)'c' }, slice.ToArray());
    }

    [Fact]
    public void Static_environment_members_methods_and_value_providers_execute()
    {
        var environment = new HostEnvironment(new HostValue("value"), new IntegerProvider(41));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<HostEnvironment>()
            .Member("host", static value => value.Host)
            .Member("wrapped", static value => value.Wrapped)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithEnvironment(schema)
            .WithValueProviders();

        Assert.Equal("prefix-value", Evaluate("host.Echo('prefix-')", environment, configuration));
        Assert.Equal("value", Evaluate("host.Value", environment, configuration));
        Assert.Equal(42L, Evaluate("wrapped + 1", environment, configuration));
    }

    [Fact]
    public void Dynamic_dictionary_environment_and_host_delegate_execute()
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["answer"] = 40L,
            ["add"] = new Func<long, long, long>(static (left, right) => left + right),
            ["nullAsZero"] = new Func<long, long>(static value => value + 1),
        };
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .AllowUndefinedVariables();

        Assert.Equal(42L, Evaluate("add(answer, 2)", environment, configuration));
        Assert.Equal(1L, Evaluate("nullAsZero(nil)", environment, configuration));
    }

    [Fact]
    public async Task Shared_evaluator_is_safe_for_concurrent_program_reuse()
    {
        ExprProgram program = ExecutionTestCompiler.Compile("map(1..100, # * 2)");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<object?>[] calls = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(
                () => ExprEvaluator.Shared.Evaluate(program, cancellationToken: cancellationToken),
                cancellationToken))
            .ToArray();

        object?[] results = await Task.WhenAll(calls);

        foreach (object? result in results)
        {
            var array = Assert.IsAssignableFrom<IExprArray>(result);
            Assert.Equal(100, array.Count);
            Assert.Equal(2L, array[0]);
            Assert.Equal(200L, array[99]);
        }
    }

    [Fact]
    public void Profiling_is_per_invocation_and_does_not_mutate_the_program()
    {
        ExprProgram program = ExecutionTestCompiler.Compile("1 + 2", profiling: true);
        var options = new ExprEvaluationOptions { EnableProfiling = true };

        ExprEvaluationResult first = ExprEvaluator.Shared.EvaluateDetailed(
            program,
            options: options,
            cancellationToken: TestContext.Current.CancellationToken);
        ExprEvaluationResult second = ExprEvaluator.Shared.EvaluateDetailed(
            program,
            options: options,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3L, first.Value);
        Assert.Equal(program.ProfilePoints.Count, first.Profile.Count);
        Assert.All(first.Profile, static sample => Assert.Equal(1, sample.InvocationCount));
        Assert.Equal(first.Profile.Select(static sample => sample.Point),
            second.Profile.Select(static sample => sample.Point));
    }

    [Theory]
    [InlineData("[1, 2, 3, 4][1:3]")]
    [InlineData("filter(1..10, # % 2 == 0)")]
    [InlineData("sum(map(1..10, # * 2))")]
    [InlineData("all(1..10, # > 0) && any(1..10, # == 7)")]
    public void Optimized_and_unoptimized_programs_evaluate_equivalently(string source)
    {
        ExprProgram optimized = ExecutionTestCompiler.Compile(
            source,
            ExprConfiguration.Default.WithOptimization(true));
        ExprProgram unoptimized = ExecutionTestCompiler.Compile(
            source,
            ExprConfiguration.Default.WithOptimization(false));

        object? optimizedValue = ExprEvaluator.Shared.Evaluate(
            optimized,
            cancellationToken: TestContext.Current.CancellationToken);
        object? unoptimizedValue = ExprEvaluator.Shared.Evaluate(
            unoptimized,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(ExprValue.Equal(unoptimizedValue, optimizedValue));
    }

    private static object? Evaluate(
        string expression,
        object? environment = null,
        ExprConfiguration? configuration = null) =>
        ExprEvaluator.Shared.Evaluate(
            ExecutionTestCompiler.Compile(expression, configuration),
            environment,
            configuration is null ? null : ExprEvaluationOptions.FromConfiguration(configuration),
            TestContext.Current.CancellationToken);

    private sealed record HostEnvironment(HostValue Host, IntegerProvider Wrapped);

    private sealed record HostValue(string Value)
    {
        public string Echo(string prefix) => prefix + Value;
    }

    private sealed record IntegerProvider(long Value) : IExprValueProvider<long>
    {
        public long ToExprValue() => Value;
    }
}
