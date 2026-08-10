using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Syntax;

public sealed class ParserTests
{
    private readonly SyntaxParser parser = new();

    [Theory]
    [InlineData("a")]
    [InlineData("foo(bar())")]
    [InlineData("foo.bar('arg1', 2, true)")]
    [InlineData("a.b().c().d[33]")]
    [InlineData("[a, b, c,]")]
    [InlineData("{foo:1, bar:2,}")]
    [InlineData("[1].foo")]
    [InlineData("{foo:1}.bar")]
    [InlineData("foo matches regex")]
    [InlineData("foo not matches 'foo'")]
    [InlineData("foo startsWith 'foo'")]
    [InlineData("foo endsWith 'foo'")]
    [InlineData("1..9")]
    [InlineData("0 in []")]
    [InlineData("not in_var")]
    [InlineData("-1 not in [1, 2, 3, 4]")]
    [InlineData("2==2 ? false : 3 not in [1, 2, 5]")]
    [InlineData("all(Tickets, #)")]
    [InlineData("all(Tickets, {.Price > 0})")]
    [InlineData("one(Tickets, {#.Price > 0})")]
    [InlineData("filter(Prices, {# > 100})")]
    [InlineData("array[1:2]")]
    [InlineData("array[:2]")]
    [InlineData("array[1:]")]
    [InlineData("array[:]")]
    [InlineData("foo ?? bar ?? baz")]
    [InlineData("foo ?? (bar || baz)")]
    [InlineData("foo || bar ?? baz")]
    [InlineData("true | ok()")]
    [InlineData("map([], #index)")]
    [InlineData("::split('a,b,c', ',')[0]")]
    [InlineData("1 < 2 < 3 < 4")]
    [InlineData("1 < 2 < 3 == true")]
    [InlineData("if a { 1 } else if b { 2 } else { 3 }")]
    [InlineData("1; (2; 3)")]
    [InlineData("true ? 1 : (2; 3; 4)")]
    [InlineData("let x = true ? 1 : 2; x")]
    [InlineData("all(ls, if true { 1 } else { 2 })")]
    [InlineData("call(if true { 1 } else { 2 })")]
    [InlineData("[if true { 1 } else { 2 }]")]
    [InlineData("map(ls, {1; 2; 3})")]
    [InlineData("let x = 1; let y = 2; 3; 4; x + y")]
    [InlineData("all([true, false,], #,)")]
    [InlineData("list | all(#,)")]
    [InlineData("func(parameter1, parameter2,)")]
    [InlineData("count(items)")]
    [InlineData("count(items, .active)")]
    [InlineData("items | count()")]
    [InlineData("sortBy(items, .Name, 'desc')")]
    [InlineData("reduce(items, #acc + #, 0)")]
    [InlineData("foo?.bar.baz")]
    [InlineData("foo.bar?.baz")]
    [InlineData("foo?.bar?.baz")]
    [InlineData("!foo?.bar.baz")]
    [InlineData("foo.bar[a?.b]?.baz")]
    [InlineData("foo.bar?.[0]")]
    public void Parses_upstream_expression_corpus(string expression)
    {
        Assert.NotNull(parser.Parse(expression).Root);
    }

    [Theory]
    [InlineData("3", 3L)]
    [InlineData("0xFF", 255L)]
    [InlineData("0o600", 384L)]
    [InlineData("0b101011", 43L)]
    [InlineData("10_000_000", 10_000_000L)]
    public void Parses_integer_literals(string expression, long expected)
    {
        var node = Assert.IsType<IntegerNode>(parser.Parse(expression).Root);
        Assert.Equal(expected, node.Value);
    }

    [Fact]
    public void Honors_unary_and_binary_precedence()
    {
        var root = Assert.IsType<BinaryNode>(parser.Parse("-2^2 + 3 * 4").Root);

        Assert.Equal("+", root.Operator);
        var unary = Assert.IsType<UnaryNode>(root.Left);
        Assert.Equal("^", Assert.IsType<BinaryNode>(unary.Operand).Operator);
        Assert.Equal("*", Assert.IsType<BinaryNode>(root.Right).Operator);
    }

    [Fact]
    public void Parses_all_literal_container_forms()
    {
        var root = Assert.IsType<MapNode>(parser.Parse("{foo: [nil, true, 2.5, b'\\xff'], ('x' + 'y'): 2,}").Root);

        Assert.Equal(2, root.Pairs.Count);
        var array = Assert.IsType<ArrayNode>(root.Pairs[0].Value);
        Assert.Collection(
            array.Elements,
            item => Assert.IsType<NilNode>(item),
            item => Assert.True(Assert.IsType<BooleanNode>(item).Value),
            item => Assert.Equal(2.5, Assert.IsType<FloatNode>(item).Value),
            item => Assert.Equal(new byte[] { 255 }, Assert.IsType<BytesNode>(item).Value.ToArray()));
        Assert.IsType<BinaryNode>(root.Pairs[1].Key);
    }

    [Fact]
    public void Parses_members_methods_slices_and_optional_chains()
    {
        var root = Assert.IsType<SliceNode>(parser.Parse("foo.bar()?.items?.[1:4]").Root);

        Assert.Equal(1, Assert.IsType<IntegerNode>(root.From).Value);
        Assert.Equal(4, Assert.IsType<IntegerNode>(root.To).Value);
        var inner = Assert.IsType<ChainNode>(root.Target);
        var items = Assert.IsType<MemberNode>(inner.Expression);
        Assert.True(items.Optional);
    }

    [Fact]
    public void Parses_predicates_pointers_and_named_pointers()
    {
        var root = Assert.IsType<BuiltinNode>(parser.Parse("filter(users, {.active && #index > 0; #acc})").Root);

        Assert.Equal("filter", root.Name);
        var predicate = Assert.IsType<PredicateNode>(root.Arguments[1]);
        var sequence = Assert.IsType<SequenceNode>(predicate.Body);
        Assert.NotNull(SyntaxWalker.Find(sequence, node => node is PointerNode { Name: "index" }));
        Assert.NotNull(SyntaxWalker.Find(sequence, node => node is PointerNode { Name: "acc" }));
    }

    [Fact]
    public void Parses_pipe_as_first_builtin_argument()
    {
        var root = Assert.IsType<BuiltinNode>(parser.Parse("users | filter(.active)").Root);

        Assert.Equal("filter", root.Name);
        Assert.IsType<IdentifierNode>(root.Arguments[0]);
        Assert.IsType<PredicateNode>(root.Arguments[1]);
    }

    [Fact]
    public void Parses_let_if_else_and_ternary_conditionals()
    {
        var variable = Assert.IsType<VariableDeclaratorNode>(
            parser.Parse("let x = 1; if x > 0 { x; x + 1 } else if x == 0 { 0 } else { -1 }").Root);

        Assert.Equal("x", variable.Name);
        var conditional = Assert.IsType<ConditionalNode>(variable.Body);
        Assert.False(conditional.IsTernary);
        Assert.IsType<SequenceNode>(conditional.WhenTrue);
        Assert.IsType<ConditionalNode>(conditional.WhenFalse);

        var ternary = Assert.IsType<ConditionalNode>(parser.Parse("x ?: 42").Root);
        Assert.True(ternary.IsTernary);
        Assert.Same(ternary.Condition, ternary.WhenTrue);
    }

    [Fact]
    public void Expands_chained_comparisons()
    {
        var root = Assert.IsType<BinaryNode>(parser.Parse("1 < x <= 10").Root);

        Assert.Equal("&&", root.Operator);
        Assert.Equal("<", Assert.IsType<BinaryNode>(root.Left).Operator);
        Assert.Equal("<=", Assert.IsType<BinaryNode>(root.Right).Operator);
    }

    [Fact]
    public void Parses_negated_word_operator()
    {
        var root = Assert.IsType<UnaryNode>(parser.Parse("name not contains 'x'").Root);

        Assert.Equal("not", root.Operator);
        Assert.Equal("contains", Assert.IsType<BinaryNode>(root.Operand).Operator);
    }

    [Fact]
    public void Distinguishes_builtins_overrides_and_global_escape()
    {
        var builtin = Assert.IsType<BuiltinNode>(parser.Parse("len(items)").Root);
        Assert.Equal("len", builtin.Name);

        var options = new SyntaxParserOptions { OverriddenBuiltins = new HashSet<string>(StringComparer.Ordinal) { "len" } };
        Assert.IsType<CallNode>(parser.Parse("len(items)", options).Root);
        Assert.IsType<BuiltinNode>(parser.Parse("::len(items)", options).Root);
    }

    [Fact]
    public void Supports_custom_if_function_mode()
    {
        var options = new SyntaxParserOptions { DisableIfOperator = true };

        Assert.IsType<CallNode>(parser.Parse("if(true, 1, 2)", options).Root);
    }

    [Fact]
    public void Enforces_node_limit()
    {
        var options = new SyntaxParserOptions { MaximumNodeCount = 2 };

        var error = Assert.Throws<SyntaxException>(() => parser.Parse("1 + 2", options));
        Assert.Contains("maximum allowed nodes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_parse_returns_structured_diagnostic()
    {
        var success = parser.TryParse("foo +", out var tree, out var diagnostic);

        Assert.False(success);
        Assert.Null(tree);
        Assert.NotNull(diagnostic);
        Assert.Equal(1, diagnostic.Line);
    }

    [Theory]
    [InlineData("foo.", "unexpected end of expression")]
    [InlineData("a+", "unexpected token EOF")]
    [InlineData("[a b]", "unexpected token Identifier(\"b\")")]
    [InlineData("foo ?? bar || baz", "cannot be mixed")]
    [InlineData("map(ls, 1; 2; 3)", "wrap predicate with brackets")]
    [InlineData("list | all(#,,)", "unexpected token Operator(\",\")")]
    public void Rejects_upstream_error_corpus(string expression, string expectedMessage)
    {
        var error = Assert.Throws<SyntaxException>(() => parser.Parse(expression));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0b15")]
    [InlineData("0X10G")]
    [InlineData("0b1E+6")]
    [InlineData("1E")]
    public void Rejects_malformed_numbers(string expression)
    {
        Assert.Throws<SyntaxException>(() => parser.Parse(expression));
    }

    [Fact]
    public void Enforces_parse_depth_even_when_parentheses_create_no_nodes()
    {
        var options = new SyntaxParserOptions { MaximumParseDepth = 4 };

        var error = Assert.Throws<SyntaxException>(() => parser.Parse("((((((1))))))", options));

        Assert.Contains("maximum parse depth", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_node_does_not_expose_its_backing_storage()
    {
        var node = Assert.IsType<BytesNode>(parser.Parse("b'abc'").Root);
        var exposed = node.Value;
        Assert.True(MemoryMarshal.TryGetArray(exposed, out var segment));

        segment.Array![segment.Offset] = (byte)'z';

        Assert.Equal(new byte[] { (byte)'a', (byte)'b', (byte)'c' }, node.Value.ToArray());
    }

    [Fact]
    public void Snapshots_mutable_option_sets_at_parse_entry()
    {
        var disabled = new HashSet<string>(StringComparer.Ordinal) { "len" };
        var options = new SyntaxParserOptions { DisabledBuiltins = disabled };

        var root = parser.Parse("len(items)", options).Root;
        disabled.Clear();

        Assert.IsType<CallNode>(root);
    }

    [Theory]
    [InlineData("all()", "expected at least 2 arguments")]
    [InlineData("items | all()", "expected at least 1 arguments")]
    [InlineData("sortBy()", "expected at least 3 arguments")]
    [InlineData("items | sortBy()", "expected at least 2 arguments")]
    [InlineData("count()", "expected at least 2 arguments")]
    public void Reports_upstream_predicate_arity_errors(string expression, string expectedMessage)
    {
        var error = Assert.Throws<SyntaxException>(() => parser.Parse(expression));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }
}
