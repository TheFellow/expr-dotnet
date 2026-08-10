using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Expr.Configuration;

namespace Expr.Benchmarks;

/// <summary>Measures the complete source-to-bytecode compilation pipeline.</summary>
[MemoryDiagnoser]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet generates a separate assembly that must access benchmark types.")]
public class CompilationBenchmarks
{
    private const string PolicyExpression =
        "(Origin == 'MOW' || Country == 'RU') && (Value >= 100 || Adults == 1) && Active";
    private const string PredicateExpression = "len(filter(Values, # % 7 == 0)) > 100";

    private readonly ExprConfiguration optimized = BenchmarkFixtures.Configuration.WithOptimization(true);
    private readonly ExprConfiguration unoptimized = BenchmarkFixtures.Configuration.WithOptimization(false);

    /// <summary>Measures a full parse, check, optimize, and compile lifecycle for a typical policy.</summary>
    /// <returns>The reusable compiled expression.</returns>
    [Benchmark]
    public CompiledExpression ColdCompilePolicy() => ExprEngine.Compile(PolicyExpression, optimized);

    /// <summary>Measures predicate compilation without optimizer passes.</summary>
    /// <returns>The reusable compiled expression.</returns>
    [Benchmark(Baseline = true)]
    public CompiledExpression CompilePredicateUnoptimized() =>
        ExprEngine.Compile(PredicateExpression, unoptimized);

    /// <summary>Measures the same predicate compilation with the complete optimizer pipeline.</summary>
    /// <returns>The reusable compiled expression.</returns>
    [Benchmark]
    public CompiledExpression CompilePredicateOptimized() =>
        ExprEngine.Compile(PredicateExpression, optimized);
}
