using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Expr.Syntax;

namespace Expr.Benchmarks;

/// <summary>Measures the public syntax pipeline independently of checking and execution.</summary>
[MemoryDiagnoser]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet generates a separate assembly that must access benchmark types.")]
public class SyntaxBenchmarks
{
    private const string SmallExpression = "Price >= 10 && Active";
    private const string PredicateExpression =
        "let adults = filter(users, .age >= 18); all(adults, {.active && #index >= 0})";

    private readonly SyntaxLexer lexer = new();
    private readonly SyntaxParser parser = new();
    private readonly CountingVisitor visitor = new();
    private SyntaxNode predicateRoot = new NilNode(default);

    /// <summary>Builds the tree reused by the walking benchmark.</summary>
    [GlobalSetup]
    public void Setup() => predicateRoot = parser.Parse(PredicateExpression).Root;

    /// <summary>Measures tokenization of a small expression.</summary>
    /// <returns>The produced tokens.</returns>
    [Benchmark(Baseline = true)]
    public IReadOnlyList<SyntaxToken> LexSmall() => lexer.Lex(SmallExpression);

    /// <summary>Measures parsing of a small expression.</summary>
    /// <returns>The produced tree.</returns>
    [Benchmark]
    public SyntaxTree ParseSmall() => parser.Parse(SmallExpression);

    /// <summary>Measures parsing of bindings and nested predicates.</summary>
    /// <returns>The produced tree.</returns>
    [Benchmark]
    public SyntaxTree ParsePredicate() => parser.Parse(PredicateExpression);

    /// <summary>Measures post-order traversal without allocating a node list.</summary>
    /// <returns>The number of nodes observed.</returns>
    [Benchmark]
    public int WalkPredicate()
    {
        visitor.Reset();
        SyntaxWalker.Walk(predicateRoot, visitor);
        return visitor.Count;
    }

    private sealed class CountingVisitor : ISyntaxVisitor
    {
        internal int Count { get; private set; }

        public void Visit(SyntaxNode node)
        {
            _ = node;
            Count++;
        }

        internal void Reset() => Count = 0;
    }
}
