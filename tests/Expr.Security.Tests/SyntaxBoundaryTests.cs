using System;
using System.Collections.Generic;
using Expr.Runtime;
using Expr.Syntax;
using Xunit;

namespace Expr.Security.Tests;

public sealed class SyntaxBoundaryTests
{
    [Fact]
    public void Walker_handles_a_very_deep_tree_iteratively()
    {
        const int depth = 25_000;
        SyntaxNode root = new IntegerNode(1, default);
        for (var index = 0; index < depth; index++)
        {
            root = new UnaryNode("-", root, default);
        }

        var visitor = new CountingVisitor();
        SyntaxWalker.Walk(root, visitor);

        Assert.Equal(depth + 1, visitor.Count);
    }

    [Fact]
    public void Recursive_rewriter_rejects_depth_before_exhausting_the_process_stack()
    {
        SyntaxNode root = new IntegerNode(1, default);
        for (var index = 0; index < 1_024; index++)
        {
            root = new UnaryNode("-", root, default);
        }

        var rewriter = new IdentityRewriter { MaximumDepth = 128 };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => rewriter.Visit(root));
        Assert.Contains("depth limit of 128", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_syntax_and_runtime_collection_constructors_snapshot_mutable_inputs()
    {
        var syntaxValues = new List<SyntaxNode> { new IntegerNode(1, default) };
        var runtimeValues = new List<object?> { 1L };
        byte[] bytes = [1, 2, 3];
        var arrayNode = new ArrayNode(syntaxValues, default);
        var byteNode = new BytesNode(bytes, default);
        var runtimeArray = new ExprArray(runtimeValues);
        var runtimeMapEntries = new List<KeyValuePair<object?, object?>>
        {
            new("answer", 42L),
        };
        var runtimeMap = new ExprMap(runtimeMapEntries);

        syntaxValues.Add(new IntegerNode(2, default));
        runtimeValues.Add(2L);
        runtimeMapEntries[0] = new KeyValuePair<object?, object?>("answer", 0L);
        bytes[0] = 99;

        Assert.Single(arrayNode.Elements);
        Assert.Equal(new byte[] { 1, 2, 3 }, byteNode.Value.ToArray());
        Assert.Single(runtimeArray);
        Assert.True(runtimeMap.TryGetValue("answer", out object? value));
        Assert.Equal(42L, value);
    }

    private sealed class CountingVisitor : ISyntaxVisitor
    {
        public int Count { get; private set; }

        public void Visit(SyntaxNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            Count++;
        }
    }

    private sealed class IdentityRewriter : SyntaxRewriter;
}
