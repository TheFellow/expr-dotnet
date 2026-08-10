using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void Traverse_supports_preorder_postorder_and_delegate_visitors()
    {
        var root = new SyntaxParser().Parse("foo + bar").Root;
        var delegated = new List<string>();

        var preOrder = SyntaxWalker.Traverse(root, SyntaxTraversalOrder.PreOrder).Select(NodeLabel).ToArray();
        var postOrder = SyntaxWalker.Traverse(root, SyntaxTraversalOrder.PostOrder).Select(NodeLabel).ToArray();
        SyntaxWalker.Walk(root, node => delegated.Add(NodeLabel(node)));

        Assert.Equal(["+", "foo", "bar"], preOrder);
        Assert.Equal(["foo", "bar", "+"], postOrder);
        Assert.Equal(postOrder, delegated);
    }

    [Fact]
    public void GetChildren_includes_optimizer_map_metadata_after_arguments()
    {
        var argument = new IdentifierNode("items", default);
        var map = new PointerNode(string.Empty, default);
        var builtin = new BuiltinNode("filter", [argument], default, map: map);

        var children = SyntaxWalker.GetChildren(builtin);

        Assert.Equal(2, children.Count);
        Assert.Same(argument, children[0]);
        Assert.Same(map, children[1]);
    }

    [Fact]
    public void Walker_enforces_configured_depth_and_node_limits()
    {
        var root = new UnaryNode("!", new UnaryNode("!", new NilNode(default), default), default);

        var depthError = Assert.Throws<InvalidOperationException>(() =>
            SyntaxWalker.Traverse(root, options: new SyntaxWalkerOptions { MaximumDepth = 2 }).ToArray());
        var countError = Assert.Throws<InvalidOperationException>(() =>
            SyntaxWalker.Traverse(root, options: new SyntaxWalkerOptions { MaximumNodeCount = 2 }).ToArray());

        Assert.Contains("depth limit", depthError.Message, StringComparison.Ordinal);
        Assert.Contains("node limit", countError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tree_patcher_replaces_shared_target_and_minimally_copies_ancestors()
    {
        var target = new IdentifierNode("value", new SourceLocation(3, 8));
        var untouched = new IntegerNode(1, default);
        var root = new ArrayNode([target, untouched, target], default);

        var rewritten = Assert.IsType<ArrayNode>(
            SyntaxPatcher.Replace(root, target, new NilNode(new SourceLocation(100, 200))));

        Assert.NotSame(root, rewritten);
        Assert.Equal(target.Location, Assert.IsType<NilNode>(rewritten.Elements[0]).Location);
        Assert.Same(untouched, rewritten.Elements[1]);
        Assert.Equal(target.Location, Assert.IsType<NilNode>(rewritten.Elements[2]).Location);
        Assert.Same(root, SyntaxPatcher.Replace(root, new IdentifierNode("missing", default), new NilNode(default)));
    }

    private static string NodeLabel(SyntaxNode node) => node switch
    {
        IdentifierNode identifier => identifier.Name,
        BinaryNode binary => binary.Operator,
        _ => node.GetType().Name,
    };

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
