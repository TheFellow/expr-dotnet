using System;
using System.Collections.Generic;
using System.Globalization;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.NativeAot;

internal static class Program
{
    private static readonly IReadOnlyList<long> SampleScores = Array.AsReadOnly([2L, 3L, 5L]);

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
        SyntaxTree tree = new SyntaxParser().Parse("answer == 42 && name == 'Ada'");
        var visitor = new CountingVisitor();
        SyntaxWalker.Walk(tree.Root, visitor);
        var scores = (IExprArray)schema.Read(environment, "scores")!;
        var totals = (IExprMap)schema.Read(environment, "totals")!;
        var expectedScores = new ExprArray([2L, 3L, 5L]);
        var expectedTotals = new ExprMap([new KeyValuePair<object?, object?>("accepted", 42L)]);

        if (schema.EnvironmentType != typeof(SampleEnvironment) ||
            schema.Members.Count != 4 ||
            schema.Read(environment, "answer") is not 42L ||
            !string.Equals(schema.Read(environment, "name") as string, "Ada", StringComparison.Ordinal) ||
            visitor.Count != 7 ||
            !ExprValue.Equal(scores, expectedScores) ||
            !ExprValue.Equal(totals, expectedTotals) ||
            !totals.TryGetValue("accepted", out object? accepted) ||
            accepted is not 42L)
        {
            return 1;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Expr.NativeAot smoke passed: {visitor.Count} nodes, {schema.Members.Count} members."));
        return 0;
    }

    private readonly record struct SampleEnvironment(
        long Answer,
        string Name,
        IReadOnlyList<long> Scores,
        IReadOnlyDictionary<string, long> Totals);

    private sealed class CountingVisitor : ISyntaxVisitor
    {
        public int Count { get; private set; }

        public void Visit(SyntaxNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            Count++;
        }
    }
}
