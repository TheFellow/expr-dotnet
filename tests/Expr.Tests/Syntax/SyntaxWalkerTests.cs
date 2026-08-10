using System;
using System.Collections.Generic;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Syntax;

public sealed class SyntaxWalkerTests
{
    [Fact]
    public void Walk_is_post_order()
    {
        var root = new BinaryNode(
            "+",
            new IdentifierNode("foo", new SourceLocation(0, 3)),
            new IdentifierNode("bar", new SourceLocation(6, 9)),
            new SourceLocation(4, 5));
        var visitor = new RecordingVisitor();

        SyntaxWalker.Walk(root, visitor);

        Assert.Equal(["foo", "bar", "+"], visitor.Values);
    }

    [Fact]
    public void Rewriter_is_non_mutating_and_patch_preserves_location()
    {
        var root = Assert.IsType<BinaryNode>(new SyntaxParser().Parse("foo + bar").Root);

        var rewritten = Assert.IsType<BinaryNode>(new IdentifierToNilRewriter().Visit(root));

        Assert.IsType<IdentifierNode>(root.Left);
        var replacement = Assert.IsType<NilNode>(rewritten.Left);
        Assert.Equal(root.Left.Location, replacement.Location);
        Assert.IsType<NilNode>(rewritten.Right);
    }

    [Fact]
    public void Walker_handles_deep_trees_without_recursive_stack_growth()
    {
        SyntaxNode root = new NilNode(default);
        for (var index = 0; index < 20_000; index++)
        {
            root = new UnaryNode("!", root, default);
        }

        var visitor = new CountingVisitor();
        SyntaxWalker.Walk(root, visitor);

        Assert.Equal(20_001, visitor.Count);
    }

    [Fact]
    public void No_op_rewriter_preserves_all_references()
    {
        var root = new SyntaxParser().Parse("{items: [foo, bar], total: len(xs)}").Root;

        var rewritten = new NoOpRewriter().Visit(root);

        Assert.Same(root, rewritten);
    }

    [Fact]
    public void Rewriter_enforces_depth_limit()
    {
        var root = new UnaryNode("!", new UnaryNode("!", new NilNode(default), default), default);
        var rewriter = new NoOpRewriter { MaximumDepth = 2 };

        var error = Assert.Throws<InvalidOperationException>(() => rewriter.Visit(root));

        Assert.Contains("depth limit", error.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingVisitor : ISyntaxVisitor
    {
        internal List<string> Values { get; } = [];

        public void Visit(SyntaxNode node)
        {
            Values.Add(node switch
            {
                IdentifierNode identifier => identifier.Name,
                BinaryNode binary => binary.Operator,
                _ => node.GetType().Name,
            });
        }
    }

    private sealed class IdentifierToNilRewriter : SyntaxRewriter
    {
        protected override SyntaxNode VisitNode(SyntaxNode node) => node is IdentifierNode
            ? Patch(node, new NilNode(default))
            : node;
    }

    private sealed class NoOpRewriter : SyntaxRewriter;

    private sealed class CountingVisitor : ISyntaxVisitor
    {
        internal int Count { get; private set; }

        public void Visit(SyntaxNode node)
        {
            Assert.NotNull(node);
            Count++;
        }
    }
}
