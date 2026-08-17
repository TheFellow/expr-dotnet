using System;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Syntax;

public sealed class SyntaxDumperTests
{
    [Fact]
    public void Dump_is_deterministic_and_source_independent_by_default()
    {
        var left = new SyntaxParser().Parse("foo + 42").Root;
        var right = new BinaryNode(
            "+",
            new IdentifierNode("foo", new SourceLocation(100, 103)),
            new IntegerNode(42, new SourceLocation(500, 502)),
            new SourceLocation(300, 301));

        var first = SyntaxDumper.Dump(left);
        var second = SyntaxDumper.Dump(right);

        Assert.Equal(first, second);
        Assert.Contains("BinaryNode", first, StringComparison.Ordinal);
        Assert.Contains("Operator: \"+\"", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Location", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_can_include_locations_and_optimizer_metadata()
    {
        SyntaxNode throwing = ExprEngine.Compile("filter([1], # > 0)[0]").SyntaxTree.Root;
        SyntaxNode mapped = ExprEngine.Compile("map(filter([1], # > 0), # * 2)").SyntaxTree.Root;
        SyntaxNode threshold = ExprEngine.Compile("count([1], # > 0) >= 3").SyntaxTree.Root;
        var node = new ArrayNode([throwing, mapped, threshold], new SourceLocation(1, 9));

        var dump = SyntaxDumper.Dump(node, new SyntaxDumperOptions { IncludeLocations = true });

        Assert.Contains("Location: 1..9", dump, StringComparison.Ordinal);
        Assert.Contains("Throws: true", dump, StringComparison.Ordinal);
        Assert.Contains("Map:", dump, StringComparison.Ordinal);
        Assert.Contains("Threshold: 3", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_covers_every_public_node_kind()
    {
        var root = new SequenceNode(
            [
                new NilNode(default),
                new IdentifierNode("x", default),
                new IntegerNode(1, default),
                new FloatNode(1.5, default),
                new BooleanNode(true, default),
                new StringNode("x", default),
                new BytesNode([1], default),
                new ConstantNode(1, default),
                new UnaryNode("!", new BooleanNode(false, default), default),
                new BinaryNode("+", new IntegerNode(1, default), new IntegerNode(2, default), default),
                new ChainNode(new IdentifierNode("x", default), default),
                new MemberNode(new IdentifierNode("x", default), new StringNode("y", default), false, true, default),
                new SliceNode(new IdentifierNode("x", default), null, null, default),
                new CallNode(new IdentifierNode("f", default), [], default),
                new BuiltinNode("len", [], default),
                new PredicateNode(new PointerNode(string.Empty, default), default),
                new PointerNode("index", default),
                new ConditionalNode(new BooleanNode(true, default), new IntegerNode(1, default), new IntegerNode(2, default), true, default),
                new VariableDeclaratorNode("x", new IntegerNode(1, default), new IdentifierNode("x", default), default),
                new ArrayNode([], default),
                new MapNode([new PairNode(new StringNode("x", default), new IntegerNode(1, default), default)], default),
            ],
            default);

        var dump = SyntaxDumper.Dump(root);

        foreach (var type in new[]
        {
            typeof(NilNode), typeof(IdentifierNode), typeof(IntegerNode), typeof(FloatNode),
            typeof(BooleanNode), typeof(StringNode), typeof(BytesNode), typeof(ConstantNode),
            typeof(UnaryNode), typeof(BinaryNode), typeof(ChainNode), typeof(MemberNode),
            typeof(SliceNode), typeof(CallNode), typeof(BuiltinNode), typeof(PredicateNode),
            typeof(PointerNode), typeof(ConditionalNode), typeof(VariableDeclaratorNode),
            typeof(SequenceNode), typeof(ArrayNode), typeof(MapNode), typeof(PairNode),
        })
        {
            Assert.Contains(type.Name, dump, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Dump_enforces_depth_and_node_limits()
    {
        var node = new UnaryNode("!", new UnaryNode("!", new NilNode(default), default), default);

        var depthError = Assert.Throws<InvalidOperationException>(() =>
            SyntaxDumper.Dump(node, new SyntaxDumperOptions { MaximumDepth = 2 }));
        var countError = Assert.Throws<InvalidOperationException>(() =>
            SyntaxDumper.Dump(node, new SyntaxDumperOptions { MaximumNodeCount = 2 }));

        Assert.Contains("depth limit", depthError.Message, StringComparison.Ordinal);
        Assert.Contains("node limit", countError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_options_reject_invalid_state_immediately()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntaxDumperOptions { MaximumDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntaxDumperOptions { MaximumNodeCount = 0 });
    }
}
