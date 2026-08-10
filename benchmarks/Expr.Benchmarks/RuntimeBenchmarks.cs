using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Expr.Runtime;

namespace Expr.Benchmarks;

/// <summary>Measures host-value operations used by the virtual machine.</summary>
[MemoryDiagnoser]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet generates a separate assembly that must access benchmark types.")]
public class RuntimeBenchmarks
{
    private readonly object?[] left =
    [
        1L,
        "two",
        new object?[] { 3L, 4D, true },
        new Dictionary<string, object?> { ["five"] = 5L },
    ];

    private readonly object?[] right =
    [
        1,
        "two",
        new object?[] { 3, 4F, true },
        new Dictionary<string, object?> { ["five"] = 5 },
    ];

    private readonly IReadOnlyList<int> values = Enumerable.Range(0, 100).ToArray();

    /// <summary>Measures iterative nested equality across compatible host numeric widths.</summary>
    /// <returns>Whether the values are equal.</returns>
    [Benchmark]
    public bool NestedEquality() => ExprValue.Equal(left, right);

    /// <summary>Measures lookup through a generic read-only collection adapter.</summary>
    /// <returns>The final list value.</returns>
    [Benchmark]
    public object? ReadOnlyListIndex()
    {
        _ = ExprCollections.TryAsArray(values, out IExprArray? array);
        return array![99];
    }
}
