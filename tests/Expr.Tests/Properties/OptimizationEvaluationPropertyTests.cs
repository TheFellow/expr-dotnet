using System;
using Expr.Configuration;
using Expr.Execution;
using Xunit;

namespace Expr.Tests.Properties;

// Provenance: inspiration/expr/optimizer/optimizer_test.go compares optimized
// and unoptimized execution. This expands that invariant with deterministic,
// seed-addressable scalar programs suitable for every CI run.
public sealed class OptimizationEvaluationPropertyTests
{
    private static readonly ulong[] Seeds =
    [
        0x0000000000c0ffeeUL,
        0x9e3779b97f4a7c15UL,
        0xd1b54a32d192ed03UL,
        0x94d049bb133111ebUL,
    ];

    private static readonly ExprEvaluationOptions EvaluationLimits = new()
    {
        MemoryBudget = 100_000,
        WorkBudget = 100_000,
        MaximumStackDepth = 512,
        MaximumScopeDepth = 64,
        MaximumCollectionLength = 1_024,
    };

    [Fact]
    public void Optimization_preserves_generated_scalar_results()
    {
        ExprConfiguration optimized = ExprConfiguration.Default
            .WithMaximumNodeCount(1_024)
            .WithMaximumCheckDepth(128)
            .WithMemoryBudget(EvaluationLimits.MemoryBudget)
            .WithOptimization(true);
        ExprConfiguration unoptimized = optimized.WithOptimization(false);

        foreach (ulong seed in Seeds)
        {
            var generator = new DeterministicExpressionGenerator(seed);
            for (var index = 0; index < 64; index++)
            {
                string source = generator.GenerateScalarExpression(maximumDepth: 3);
                object? expected = ExprEngine.Evaluate(
                    source,
                    configuration: unoptimized,
                    evaluationOptions: EvaluationLimits,
                    cancellationToken: TestContext.Current.CancellationToken);
                object? actual = ExprEngine.Evaluate(
                    source,
                    configuration: optimized,
                    evaluationOptions: EvaluationLimits,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.True(
                    Equals(expected, actual),
                    $"Optimization mismatch for seed 0x{seed:x16}, case {index}: {source}; expected {expected}, got {actual}.");
            }
        }
    }
}
