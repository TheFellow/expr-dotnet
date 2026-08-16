using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Syntax;

public sealed class SyntaxPrinterTests
{
    private static readonly int[] ConstantIntegers = [1, 2];

    public static TheoryData<string, string> UpstreamPrintCases => new()
    {
        { "nil", "nil" },
        { "true", "true" },
        { "false", "false" },
        { "1", "1" },
        { "1.1", "1.1" },
        { "\"a\"", "\"a\"" },
        { "'a'", "\"a\"" },
        { "a", "a" },
        { "a.b", "a.b" },
        { "a[0]", "a[0]" },
        { "a[\"the b\"]", "a[\"the b\"]" },
        { "a.b[0]", "a.b[0]" },
        { "a?.b", "a?.b" },
        { "x[0][1]", "x[0][1]" },
        { "x?.[0]?.[1]", "x?.[0]?.[1]" },
        { "-a", "-a" },
        { "!a", "!a" },
        { "not a", "not a" },
        { "a + b", "a + b" },
        { "a + b * c", "a + b * c" },
        { "(a + b) * c", "(a + b) * c" },
        { "a * (b + c)", "a * (b + c)" },
        { "-(a + b) * c", "-(a + b) * c" },
        { "a == b", "a == b" },
        { "a matches b", "a matches b" },
        { "a in b", "a in b" },
        { "a not in b", "not (a in b)" },
        { "a and b", "a and b" },
        { "a or b", "a or b" },
        { "a or b and c", "a or (b and c)" },
        { "a or (b and c)", "a or (b and c)" },
        { "(a or b) and c", "(a or b) and c" },
        { "a ? b : c", "a ? b : c" },
        { "a ? b : c ? d : e", "a ? b : (c ? d : e)" },
        { "(a ? b : c) ? d : e", "(a ? b : c) ? d : e" },
        { "a ? (b ? c : d) : e", "a ? (b ? c : d) : e" },
        { "func()", "func()" },
        { "func(a)", "func(a)" },
        { "func(a, b)", "func(a, b)" },
        { "{}", "{}" },
        { "{a: b}", "{a: b}" },
        { "{a: b, c: d}", "{a: b, c: d}" },
        { "{\"a\": b, 'c': d}", "{a: b, c: d}" },
        { "{\"a\": b, 8: 8}", "{a: b, \"8\": 8}" },
        { "{\"9\": 9, '8': 8, \"foo\": d}", "{\"9\": 9, \"8\": 8, foo: d}" },
        { "[]", "[]" },
        { "[a]", "[a]" },
        { "[a, b]", "[a, b]" },
        { "len(a)", "len(a)" },
        { "map(a, # > 0)", "map(a, # > 0)" },
        { "map(a, {# > 0})", "map(a, # > 0)" },
        { "map(a, .b)", "map(a, .b)" },
        { "a.b()", "a.b()" },
        { "a.b(c)", "a.b(c)" },
        { "a[1:-1]", "a[1:-1]" },
        { "a[1:]", "a[1:]" },
        { "a[:1]", "a[:1]" },
        { "a[:]", "a[:]" },
        { "(nil ?? 1) > 0", "(nil ?? 1) > 0" },
        { "{(\"a\" + \"b\"): 42}", "{(\"a\" + \"b\"): 42}" },
        { "(One == 1 ? true : false) && Two == 2", "(One == 1 ? true : false) && Two == 2" },
        { "not (a == 1 ? b > 1 : b < 2)", "not (a == 1 ? b > 1 : b < 2)" },
        { "(-(1+1)) ** 2", "(-(1 + 1)) ** 2" },
        { "2 ** (-(1+1))", "2 ** -(1 + 1)" },
        { "(2 ** 2) ** 3", "(2 ** 2) ** 3" },
        { "(3 + 5) / (5 % 3)", "(3 + 5) / (5 % 3)" },
        { "(-(1+1)) == 2", "-(1 + 1) == 2" },
        { "if true { 1 } else { 2 }", "if true { 1 } else { 2 }" },
        { "if true { 1 } else if false { 2 } else { 3 }", "if true { 1 } else if false { 2 } else { 3 }" },
    };

    public static TheoryData<string> RoundTripCases =>
    [
        "nil",
        "identifier",
        "42",
        "1.25",
        "true",
        "\"escape\\n\\\"\\\\ 😀\"",
        "b\"bytes\\x00\\xff\"",
        "not (a + b * c)",
        "(a or b)..(c or d)",
        "(a ?? b).value",
        "(a ? b : c)[0]",
        "a?.b?.[index]",
        "items[1:-1]",
        "custom(a, b)",
        "map(items, #index > 0 ? .name : #)",
        "let value = 1; value + 2; value",
        "if ready { [1, 2] } else { {result: nil} }",
        "{(prefix + suffix): fn()}",
    ];

    [Theory]
    [MemberData(nameof(UpstreamPrintCases))]
    public void Print_matches_upstream_canonical_form(string source, string expected)
    {
        var root = new SyntaxParser().Parse(source).Root;

        var printed = SyntaxPrinter.Print(root);

        Assert.Equal(expected, printed);
        Assert.Equal(expected, root.ToString());
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Parse_print_parse_preserves_structure_without_reusing_locations(string source)
    {
        var parser = new SyntaxParser();
        var original = parser.Parse(source).Root;

        var printed = SyntaxPrinter.Print(original);
        var reparsed = parser.Parse(printed).Root;

        AssertEquivalent(original, reparsed);
        Assert.NotSame(original, reparsed);
    }

    [Fact]
    public void Sequence_predicate_prints_with_required_braces_and_round_trips()
    {
        var original = new SyntaxParser().Parse("map(items, {#; #index})").Root;

        var printed = SyntaxPrinter.Print(original);
        var reparsed = new SyntaxParser().Parse(printed).Root;

        Assert.Equal("map(items, {#; #index})", printed);
        AssertEquivalent(original, reparsed);
    }

    [Fact]
    public void Bytes_print_as_lossless_byte_escapes()
    {
        var node = new BytesNode([0x00, 0x22, 0x5c, 0x7e, 0x7f, 0xff], default);

        var printed = SyntaxPrinter.Print(node);
        var reparsed = Assert.IsType<BytesNode>(new SyntaxParser().Parse(printed).Root);

        Assert.Equal("b\"\\x00\\\"\\\\~\\x7f\\xff\"", printed);
        Assert.Equal(node.Value.ToArray(), reparsed.Value.ToArray());
    }

    [Fact]
    public void Constants_use_stable_compact_map_order_and_invariant_numbers()
    {
        var value = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["z"] = new[] { 1, 2, 3 },
            ["a"] = 1.5,
        };
        var node = new ConstantNode(value, default);

        var printed = SyntaxPrinter.Print(node);

        Assert.Equal("{\"a\":1.5,\"z\":[1,2,3]}", printed);
        _ = new SyntaxParser().Parse(printed);
    }

    [Fact]
    public void Byte_constants_print_as_byte_literals_without_changing_type()
    {
        var node = new ConstantNode(new byte[] { 0x00, 0x22, 0xff }, default);

        var printed = SyntaxPrinter.Print(node);
        var reparsed = Assert.IsType<BytesNode>(new SyntaxParser().Parse(printed).Root);

        Assert.Equal("b\"\\x00\\\"\\xff\"", printed);
        Assert.Equal(new byte[] { 0x00, 0x22, 0xff }, reparsed.Value.ToArray());
    }

    [Fact]
    public void Constants_outside_the_expr_integer_domain_fail_explicitly()
    {
        var node = new ConstantNode(ulong.MaxValue, default);

        var error = Assert.Throws<NotSupportedException>(() => SyntaxPrinter.Print(node));

        Assert.Contains("Int64.MaxValue", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Printer_is_culture_independent()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var node = new FloatNode(1234.5, default);

            Assert.Equal("1234.5", SyntaxPrinter.Print(node));
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
        }
    }

    [Fact]
    public void Integral_float_keeps_its_float_node_kind()
    {
        var node = new FloatNode(1, default);

        var printed = SyntaxPrinter.Print(node);
        var reparsed = new SyntaxParser().Parse(printed).Root;

        Assert.Equal("1.0", printed);
        Assert.IsType<FloatNode>(reparsed);
    }

    [Fact]
    public void Printer_enforces_depth_and_node_limits()
    {
        var deep = new UnaryNode("!", new UnaryNode("!", new NilNode(default), default), default);
        var wide = new ArrayNode([new NilNode(default), new NilNode(default)], default);

        var depthError = Assert.Throws<InvalidOperationException>(() =>
            SyntaxPrinter.Print(deep, new SyntaxPrinterOptions { MaximumDepth = 2 }));
        var countError = Assert.Throws<InvalidOperationException>(() =>
            SyntaxPrinter.Print(wide, new SyntaxPrinterOptions { MaximumNodeCount = 2 }));

        Assert.Contains("depth limit", depthError.Message, StringComparison.Ordinal);
        Assert.Contains("node limit", countError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Printer_rejects_cycles_in_constant_values()
    {
        var value = new List<object?>();
        value.Add(value);
        var node = new ConstantNode(value, default);

        var error = Assert.Throws<InvalidOperationException>(() => SyntaxPrinter.Print(node));

        Assert.Contains("reference cycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_public_node_kind_has_a_canonical_representation()
    {
        var location = new SourceLocation(7, 11);
        var nodes = new SyntaxNode[]
        {
            new NilNode(location),
            new IdentifierNode("item", location),
            new IntegerNode(1, location),
            new FloatNode(1.5, location),
            new BooleanNode(true, location),
            new StringNode("text", location),
            new BytesNode([1, 2], location),
            new ConstantNode(ConstantIntegers, location),
            new UnaryNode("!", new BooleanNode(false, location), location),
            new BinaryNode("+", new IntegerNode(1, location), new IntegerNode(2, location), location),
            new ChainNode(new MemberNode(new IdentifierNode("a", location), new StringNode("b", location), true, false, location), location),
            new MemberNode(new IdentifierNode("a", location), new StringNode("b", location), false, false, location),
            new SliceNode(new IdentifierNode("a", location), null, null, location),
            new CallNode(new IdentifierNode("fn", location), [], location),
            new BuiltinNode("len", [new IdentifierNode("a", location)], location),
            new PredicateNode(new PointerNode(string.Empty, location), location),
            new PointerNode("index", location),
            new ConditionalNode(new BooleanNode(true, location), new IntegerNode(1, location), new IntegerNode(2, location), true, location),
            new VariableDeclaratorNode("x", new IntegerNode(1, location), new IdentifierNode("x", location), location),
            new SequenceNode([new IntegerNode(1, location), new IntegerNode(2, location)], location),
            new ArrayNode([new IntegerNode(1, location)], location),
            new MapNode([new PairNode(new StringNode("key", location), new IntegerNode(1, location), location)], location),
            new PairNode(new StringNode("key", location), new IntegerNode(1, location), location),
        };

        var printed = nodes.Select(static node => SyntaxPrinter.Print(node)).ToArray();

        Assert.Equal(23, printed.Length);
        Assert.All(printed, static value => Assert.NotEmpty(value));
    }

    private static void AssertEquivalent(SyntaxNode expected, SyntaxNode actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case NilNode:
                break;
            case IdentifierNode left:
                Assert.Equal(left.Name, Assert.IsType<IdentifierNode>(actual).Name);
                break;
            case IntegerNode left:
                Assert.Equal(left.Value, Assert.IsType<IntegerNode>(actual).Value);
                break;
            case FloatNode left:
                Assert.Equal(left.Value, Assert.IsType<FloatNode>(actual).Value);
                break;
            case BooleanNode left:
                Assert.Equal(left.Value, Assert.IsType<BooleanNode>(actual).Value);
                break;
            case StringNode left:
                Assert.Equal(left.Value, Assert.IsType<StringNode>(actual).Value);
                break;
            case BytesNode left:
                Assert.Equal(left.Value.ToArray(), Assert.IsType<BytesNode>(actual).Value.ToArray());
                break;
            case UnaryNode left:
                var rightUnary = Assert.IsType<UnaryNode>(actual);
                Assert.Equal(left.Operator, rightUnary.Operator);
                AssertEquivalent(left.Operand, rightUnary.Operand);
                break;
            case BinaryNode left:
                var rightBinary = Assert.IsType<BinaryNode>(actual);
                Assert.Equal(left.Operator, rightBinary.Operator);
                AssertEquivalent(left.Left, rightBinary.Left);
                AssertEquivalent(left.Right, rightBinary.Right);
                break;
            case ChainNode left:
                AssertEquivalent(left.Expression, Assert.IsType<ChainNode>(actual).Expression);
                break;
            case MemberNode left:
                var rightMember = Assert.IsType<MemberNode>(actual);
                Assert.Equal(left.Optional, rightMember.Optional);
                Assert.Equal(left.IsMethod, rightMember.IsMethod);
                AssertEquivalent(left.Target, rightMember.Target);
                AssertEquivalent(left.Property, rightMember.Property);
                break;
            case SliceNode left:
                var rightSlice = Assert.IsType<SliceNode>(actual);
                AssertEquivalent(left.Target, rightSlice.Target);
                AssertOptionalEquivalent(left.From, rightSlice.From);
                AssertOptionalEquivalent(left.To, rightSlice.To);
                break;
            case CallNode left:
                var rightCall = Assert.IsType<CallNode>(actual);
                AssertEquivalent(left.Callee, rightCall.Callee);
                AssertListEquivalent(left.Arguments, rightCall.Arguments);
                break;
            case BuiltinNode left:
                var rightBuiltin = Assert.IsType<BuiltinNode>(actual);
                Assert.Equal(left.Name, rightBuiltin.Name);
                AssertListEquivalent(left.Arguments, rightBuiltin.Arguments);
                break;
            case PredicateNode left:
                AssertEquivalent(left.Body, Assert.IsType<PredicateNode>(actual).Body);
                break;
            case PointerNode left:
                Assert.Equal(left.Name, Assert.IsType<PointerNode>(actual).Name);
                break;
            case ConditionalNode left:
                var rightConditional = Assert.IsType<ConditionalNode>(actual);
                Assert.Equal(left.IsTernary, rightConditional.IsTernary);
                AssertEquivalent(left.Condition, rightConditional.Condition);
                AssertEquivalent(left.WhenTrue, rightConditional.WhenTrue);
                AssertEquivalent(left.WhenFalse, rightConditional.WhenFalse);
                break;
            case VariableDeclaratorNode left:
                var rightVariable = Assert.IsType<VariableDeclaratorNode>(actual);
                Assert.Equal(left.Name, rightVariable.Name);
                AssertEquivalent(left.Value, rightVariable.Value);
                AssertEquivalent(left.Body, rightVariable.Body);
                break;
            case SequenceNode left:
                AssertListEquivalent(left.Expressions, Assert.IsType<SequenceNode>(actual).Expressions);
                break;
            case ArrayNode left:
                AssertListEquivalent(left.Elements, Assert.IsType<ArrayNode>(actual).Elements);
                break;
            case MapNode left:
                AssertListEquivalent(left.Pairs, Assert.IsType<MapNode>(actual).Pairs);
                break;
            case PairNode left:
                var rightPair = Assert.IsType<PairNode>(actual);
                AssertEquivalent(left.Key, rightPair.Key);
                AssertEquivalent(left.Value, rightPair.Value);
                break;
            case ConstantNode:
                throw new InvalidOperationException("Parser-produced syntax never contains optimizer constants.");
            default:
                throw new ArgumentOutOfRangeException(nameof(expected), expected.GetType(), "Unknown syntax node type.");
        }
    }

    private static void AssertOptionalEquivalent(SyntaxNode? expected, SyntaxNode? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.NotNull(actual);
            AssertEquivalent(expected, actual);
        }
    }

    private static void AssertListEquivalent<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
        where T : SyntaxNode
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            AssertEquivalent(expected[index], actual[index]);
        }
    }
}
