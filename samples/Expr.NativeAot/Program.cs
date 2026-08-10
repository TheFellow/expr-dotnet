using System;
using System.Globalization;
using Expr.Runtime;
using Expr.Syntax;

namespace Expr.NativeAot;

internal static class Program
{
    private static int Main()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<SampleEnvironment>()
            .Build();
        SyntaxTree tree = new SyntaxParser().Parse("answer == 42 && name == 'Ada'");
        var visitor = new CountingVisitor();
        SyntaxWalker.Walk(tree.Root, visitor);
        var values = new ExprArray([42L, "Ada"]);
        var map = new ExprMap([new("answer", values[0]), new("name", values[1])]);

        if (schema.EnvironmentType != typeof(SampleEnvironment) ||
            schema.Members.Count != 0 ||
            visitor.Count != 7 ||
            !map.TryGetValue("answer", out object? answer) ||
            answer is not 42L)
        {
            return 1;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Expr.NativeAot smoke passed: {visitor.Count} nodes, {schema.Members.Count} members."));
        return 0;
    }

    private readonly record struct SampleEnvironment(long Answer, string Name);

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
