using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Expr.Configuration;

namespace Expr.Benchmarks;

/// <summary>Measures hot evaluation of immutable programs across representative Expr workloads.</summary>
[MemoryDiagnoser]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet generates a separate assembly that must access benchmark types.")]
public class EvaluationBenchmarks
{
    private readonly PolicyEnvironment environment = BenchmarkFixtures.Environment;
    private CompiledExpression policy = null!;
    private CompiledExpression memberAccess = null!;
    private CompiledExpression mapAccess = null!;
    private CompiledExpression constantRegex = null!;
    private CompiledExpression dynamicRegex = null!;
    private CompiledExpression filter = null!;
    private CompiledExpression filterLengthOptimized = null!;
    private CompiledExpression filterLengthUnoptimized = null!;
    private CompiledExpression filteredMap = null!;

    /// <summary>Compiles each program once so benchmark methods isolate VM execution.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ExprConfiguration optimized = BenchmarkFixtures.Configuration.WithOptimization(true);
        ExprConfiguration unoptimized = BenchmarkFixtures.Configuration.WithOptimization(false);
        policy = ExprEngine.Compile(
            "(Origin == 'MOW' || Country == 'RU') && (Value >= 100 || Adults == 1) && Active",
            optimized);
        memberAccess = ExprEngine.Compile("Price.Value > 100", optimized);
        mapAccess = ExprEngine.Compile("Labels['region'] == 'west' && Labels['tier'] == 'gold'", optimized);
        constantRegex = ExprEngine.Compile("Email matches '^[a-z]+@[a-z]+\\\\.[a-z]+$'", optimized);
        dynamicRegex = ExprEngine.Compile("Email matches Pattern", optimized);
        filter = ExprEngine.Compile("filter(Values, # % 7 == 0)", optimized);
        filterLengthOptimized = ExprEngine.Compile("len(filter(Values, # % 7 == 0))", optimized);
        filterLengthUnoptimized = ExprEngine.Compile("len(filter(Values, # % 7 == 0))", unoptimized);
        filteredMap = ExprEngine.Compile("map(filter(Values, # % 7 == 0), # * 2)", optimized);
    }

    /// <summary>Measures a hot policy evaluation over scalar environment members.</summary>
    /// <returns>The Boolean policy result.</returns>
    [Benchmark(Baseline = true)]
    public object? Policy() => policy.Run(environment);

    /// <summary>Measures a statically checked nested CLR member read.</summary>
    /// <returns>The comparison result.</returns>
    [Benchmark]
    public object? MemberAccess() => memberAccess.Run(environment);

    /// <summary>Measures two indexed map reads.</summary>
    /// <returns>The comparison result.</returns>
    [Benchmark]
    public object? MapAccess() => mapAccess.Run(environment);

    /// <summary>Measures a constant regular expression cached in the compiled program.</summary>
    /// <returns>The match result.</returns>
    [Benchmark]
    public object? ConstantRegex() => constantRegex.Run(environment);

    /// <summary>Measures a runtime-supplied regular-expression pattern.</summary>
    /// <returns>The match result.</returns>
    [Benchmark]
    public object? DynamicRegex() => dynamicRegex.Run(environment);

    /// <summary>Measures materializing the values divisible by seven from 1,000 inputs.</summary>
    /// <returns>The filtered values.</returns>
    [Benchmark]
    public object? Filter() => filter.Run(environment);

    /// <summary>Measures the optimized filter-length fusion.</summary>
    /// <returns>The number of matching values.</returns>
    [Benchmark]
    public object? FilterLengthOptimized() => filterLengthOptimized.Run(environment);

    /// <summary>Measures materializing a filter before taking its length.</summary>
    /// <returns>The number of matching values.</returns>
    [Benchmark]
    public object? FilterLengthUnoptimized() => filterLengthUnoptimized.Run(environment);

    /// <summary>Measures fused filter/map execution over 1,000 inputs.</summary>
    /// <returns>The mapped matching values.</returns>
    [Benchmark]
    public object? FilteredMap() => filteredMap.Run(environment);
}
