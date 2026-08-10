using System;
using System.Collections.Generic;
using System.Globalization;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Types;

namespace Expr.NativeAot;

internal static class Program
{
    private static readonly IReadOnlyList<long> SampleScores = Array.AsReadOnly([2L, 3L, 5L]);
    private const string SampleExpression =
        "answer == 42 && name startsWith 'Ad' && all(scores, # > 0) && totals['accepted'] == answer";

    private static int Main()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<SampleEnvironment>()
            .Member("answer", static environment => environment.Answer, ExprTypes.Integer)
            .Member("name", static environment => environment.Name, ExprTypes.String)
            .ArrayMember("scores", static environment => environment.Scores, ExprTypes.Integer)
            .MapMember(
                "totals",
                static environment => environment.Totals,
                ExprTypes.String,
                ExprTypes.Integer)
            .Build();
        var environment = new SampleEnvironment(
            42L,
            "Ada",
            SampleScores,
            new Dictionary<string, long> { ["accepted"] = 42L });
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithExpectedType(ExprTypes.Boolean, warnOnAny: true)
            .WithMemoryBudget(32_768)
            .WithMaximumNodeCount(256)
            .WithMaximumCheckDepth(64);
        CompiledExpression expression = ExprEngine.Compile(SampleExpression, configuration);
        ExprEvaluationResult result = ExprEngine.RunDetailed(
            expression,
            environment,
            new ExprEvaluationOptions
            {
                MemoryBudget = 32_768,
                WorkBudget = 10_000,
                MaximumStackDepth = 256,
                MaximumScopeDepth = 32,
                MaximumCollectionLength = 1_024,
                MaximumRegularExpressionLength = 1_024,
                RegularExpressionTimeout = TimeSpan.FromMilliseconds(100),
            });

        if (schema.EnvironmentType != typeof(SampleEnvironment) ||
            schema.Members.Count != 4 ||
            result.Value is not true ||
            result.WorkUsed is 0 ||
            result.WorkUsed > 10_000 ||
            result.MemoryUsed > 32_768)
        {
            return 1;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Expr.NativeAot evaluation passed: {expression.Program.Instructions.Count} instructions, " +
            $"{result.WorkUsed} work units, {result.MemoryUsed} memory units."));
        return 0;
    }

    private readonly record struct SampleEnvironment(
        long Answer,
        string Name,
        IReadOnlyList<long> Scores,
        IReadOnlyDictionary<string, long> Totals);
}
