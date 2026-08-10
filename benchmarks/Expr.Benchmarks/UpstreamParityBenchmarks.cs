using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Expr.Checking;
using Expr.Configuration;
using Expr.Patching;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Benchmarks;

/// <summary>Groups the remaining pinned upstream benchmark variants by the pipeline stage they measure.</summary>
[MemoryDiagnoser]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet generates a separate assembly that must access benchmark types.")]
public class UpstreamParityBenchmarks
{
    private readonly IReadOnlyDictionary<string, CompiledExpression> programs;
    private readonly IReadOnlyDictionary<string, CompiledExpression> valueProviderPrograms;
    private readonly ExprConfiguration configuration;
    private readonly BenchmarkEnvironment environment;
    private readonly ValueProviderEnvironment valueProviderEnvironment;

    /// <summary>Initializes the grouped upstream workloads once per benchmark instance.</summary>
    public UpstreamParityBenchmarks()
    {
        environment = new BenchmarkEnvironment(
            42,
            new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            new Dictionary<string, object?> { ["key"] = 42L });
        var schema = new ExprEnvironmentSchemaBuilder<BenchmarkEnvironment>()
            .Member("Value", static value => value.Value, ExprTypes.Integer)
            .Member("Values", static value => value.Values, ExprTypes.ArrayOf(ExprTypes.Integer))
            .Member("Map", static value => value.Map, new MapTypeDescriptor([], ExprTypes.String, ExprTypes.Any))
            .Build();
        var function = new ExprFunction(
            "fn",
            [new ExprFunctionOverload([ExprTypes.Integer], ExprTypes.Integer)],
            static arguments => (long)arguments[0]! + 1);
        configuration = ExprConfiguration.Default.WithEnvironment(schema).WithFunction(function);
        var compiled = new Dictionary<string, CompiledExpression>(System.StringComparer.Ordinal);
        foreach (string source in AllWorkloads())
        {
            compiled[source] = ExprEngine.Compile(source, configuration);
        }

        programs = compiled;

        valueProviderEnvironment = new ValueProviderEnvironment(
            new TypedValue(1),
            new TypedValue(2),
            new UntypedValue(1),
            new UntypedValue(2));
        ExprEnvironmentSchema valueProviderSchema = new ExprEnvironmentSchemaBuilder<ValueProviderEnvironment>()
            .Member("TypedOne", static value => value.TypedOne, new ObjectTypeDescriptor(typeof(TypedValue)))
            .Member("TypedTwo", static value => value.TypedTwo, new ObjectTypeDescriptor(typeof(TypedValue)))
            .Member("UntypedOne", static value => value.UntypedOne, new ObjectTypeDescriptor(typeof(UntypedValue)))
            .Member("UntypedTwo", static value => value.UntypedTwo, new ObjectTypeDescriptor(typeof(UntypedValue)))
            .Build();
        ExprConfiguration valueProviderConfiguration = ExprConfiguration.Default
            .WithEnvironment(valueProviderSchema)
            .WithValueProviders();
        valueProviderPrograms = new Dictionary<string, CompiledExpression>(System.StringComparer.Ordinal)
        {
            ["TypedOne + TypedTwo"] = ExprEngine.Compile("TypedOne + TypedTwo", valueProviderConfiguration),
            ["UntypedOne + UntypedTwo"] = ExprEngine.Compile("UntypedOne + UntypedTwo", valueProviderConfiguration),
        };
    }

    /// <summary>Gets collection, host-access, call, and VM execution variants from upstream benchmarks.</summary>
    public IEnumerable<string> ExecutionWorkloads()
    {
        yield return "Values[5]";
        yield return "filter(Values, # % 2 == 0)[0]";
        yield return "filter(Values, # % 2 == 0)[-1]";
        yield return "sort(Values, 'desc')";
        yield return "sortBy(Values, -#)";
        yield return "groupBy(Values, # % 2)";
        yield return "reduce(Values, #acc + #, 0)";
        yield return "min(Values)";
        yield return "max(Values)";
        yield return "mean(Values)";
        yield return "median(Values)";
        yield return "Value + 1";
        yield return "Map['key']";
        yield return "fn(Value)";
        yield return "1 + 2";
    }

    /// <summary>Gets optimized and unoptimized aggregate variants, including early and absent matches.</summary>
    public IEnumerable<string> OptimizerWorkloads()
    {
        yield return "count(Values, # > 0) > 0";
        yield return "count(Values, # > 100) > 0";
        yield return "count(Values, # > 5) >= 1";
        yield return "count(Values, # > 5) > 3";
        yield return "count(Values, # > 5) >= 3";
        yield return "count(Values, # > 5) < 9";
        yield return "count(Values, # > 5) <= 8";
        yield return "sum(Values)";
        yield return "sum(1..100)";
        yield return "reduce(1..100, #acc + #)";
    }

    /// <summary>Gets typed and dynamically typed value-provider addition variants.</summary>
    public IEnumerable<string> ValueProviderWorkloads()
    {
        yield return "TypedOne + TypedTwo";
        yield return "UntypedOne + UntypedTwo";
    }

    /// <summary>Measures hot execution for grouped upstream VM and built-in variants.</summary>
    /// <param name="source">The parameterized Expr workload.</param>
    /// <returns>The evaluated value.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(ExecutionWorkloads))]
    public object? RunUpstreamVariant(string source) => programs[source].Run(environment);

    /// <summary>Measures optimized aggregate execution and early-exit variants.</summary>
    /// <param name="source">The parameterized aggregate workload.</param>
    /// <returns>The evaluated value.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(OptimizerWorkloads))]
    public object? RunOptimizerVariant(string source) => programs[source].Run(environment);

    /// <summary>Measures semantic value-provider unwrapping during arithmetic execution.</summary>
    /// <param name="source">The typed or untyped provider workload.</param>
    /// <returns>The evaluated sum.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(ValueProviderWorkloads))]
    public object? RunValueProviderVariant(string source) => valueProviderPrograms[source].Run(valueProviderEnvironment);

    /// <summary>Measures cold parse/check/optimize/compile variants used by checker and call benchmarks.</summary>
    /// <param name="source">The source compiled on every operation.</param>
    /// <returns>The compiled expression.</returns>
    [Benchmark]
    [ArgumentsSource(nameof(ExecutionWorkloads))]
    public CompiledExpression CompileUpstreamVariant(string source) => ExprEngine.Compile(source, configuration);

    /// <summary>Measures the checker independently for the pinned checker benchmark family.</summary>
    /// <returns>The semantic model.</returns>
    [Benchmark]
    public ExprSemanticModel CheckUpstreamVariant() =>
        new ExprChecker().Check(new SyntaxParser().Parse("Value + len(Values)"), configuration);

    private IEnumerable<string> AllWorkloads()
    {
        foreach (string source in ExecutionWorkloads())
        {
            yield return source;
        }

        foreach (string source in OptimizerWorkloads())
        {
            yield return source;
        }
    }

    private sealed record BenchmarkEnvironment(
        long Value,
        IReadOnlyList<long> Values,
        IReadOnlyDictionary<string, object?> Map);

    private sealed record ValueProviderEnvironment(
        TypedValue TypedOne,
        TypedValue TypedTwo,
        UntypedValue UntypedOne,
        UntypedValue UntypedTwo);

    private sealed record TypedValue(long Value) : IExprValueProvider<long>
    {
        public long ToExprValue() => Value;
    }

    private sealed record UntypedValue(long Value) : IExprValueProvider
    {
        public object ToExprValue() => Value;
    }
}
