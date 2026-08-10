using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Properties;

// Provenance: inspiration/expr/test/fuzz/fuzz_test.go and fuzz_corpus.txt at
// 4b31df3a2e0eefec04c017a82a00e0f08541d3e4. The upstream harness bounds source
// to 1,000 bytes; these deterministic CI properties additionally bound parser
// depth and node count so malformed input cannot turn the test into a stress run.
public sealed class ParserPropertyTests
{
    private const int CasesPerSeed = 128;
    private static readonly ulong[] Seeds =
    [
        0x0000000000c0ffeeUL,
        0x9e3779b97f4a7c15UL,
        0xd1b54a32d192ed03UL,
        0x94d049bb133111ebUL,
    ];

    private static readonly SyntaxParserOptions ParserLimits = new()
    {
        MaximumNodeCount = 1_024,
        MaximumParseDepth = 128,
    };

    private static readonly SyntaxPrinterOptions PrinterLimits = new()
    {
        MaximumNodeCount = 1_024,
        MaximumDepth = 128,
    };

    private static readonly SyntaxDumperOptions DumperLimits = new()
    {
        MaximumNodeCount = 1_024,
        MaximumDepth = 128,
    };

    [Fact]
    public void Generated_syntax_is_structurally_stable_after_canonical_round_trip()
    {
        foreach (ulong seed in Seeds)
        {
            var generator = new DeterministicExpressionGenerator(seed);
            for (var index = 0; index < CasesPerSeed; index++)
            {
                string source = generator.GenerateSyntax(maximumDepth: 4);
                SyntaxNode original = new SyntaxParser().Parse(source, ParserLimits).Root;

                string canonical = SyntaxPrinter.Print(original, PrinterLimits);
                SyntaxNode reparsed = new SyntaxParser().Parse(canonical, ParserLimits).Root;
                string secondCanonical = SyntaxPrinter.Print(reparsed, PrinterLimits);
                string originalDump = SyntaxDumper.Dump(original, DumperLimits);
                string reparsedDump = SyntaxDumper.Dump(reparsed, DumperLimits);

                Assert.True(
                    string.Equals(canonical, secondCanonical, StringComparison.Ordinal),
                    $"Canonical instability for seed 0x{seed:x16}, case {index}: {source}");
                Assert.True(
                    string.Equals(originalDump, reparsedDump, StringComparison.Ordinal),
                    $"Structural instability for seed 0x{seed:x16}, case {index}: {source}\nCanonical: {canonical}\nOriginal:\n{originalDump}\nReparsed:\n{reparsedDump}");
            }
        }
    }

    [Theory]
    [InlineData("(nil)[0]")]
    [InlineData("(true)[0]")]
    [InlineData("(false).value")]
    [InlineData("(1)[0]")]
    [InlineData("(1.5).value")]
    public void Early_return_literals_remain_parenthesized_as_postfix_targets(string source)
    {
        SyntaxNode original = new SyntaxParser().Parse(source, ParserLimits).Root;

        string canonical = SyntaxPrinter.Print(original, PrinterLimits);
        SyntaxNode reparsed = new SyntaxParser().Parse(canonical, ParserLimits).Root;

        Assert.StartsWith("(", canonical, StringComparison.Ordinal);
        Assert.Equal(
            SyntaxDumper.Dump(original, DumperLimits),
            SyntaxDumper.Dump(reparsed, DumperLimits));
    }

    [Fact]
    public void Optimizer_constants_are_parenthesized_when_their_rendered_scalar_is_a_postfix_target()
    {
        var node = new MemberNode(
            new ConstantNode(42L, default),
            new IntegerNode(0, default),
            false,
            false,
            default);

        string canonical = SyntaxPrinter.Print(node, PrinterLimits);

        Assert.Equal("(42)[0]", canonical);
        _ = new SyntaxParser().Parse(canonical, ParserLimits);
    }

    [Fact]
    public void Unary_postfix_targets_remain_parenthesized_to_preserve_binding()
    {
        SyntaxNode original = new SyntaxParser().Parse("(-\"text\")[0]", ParserLimits).Root;

        string canonical = SyntaxPrinter.Print(original, PrinterLimits);
        SyntaxNode reparsed = new SyntaxParser().Parse(canonical, ParserLimits).Root;

        Assert.Equal("(-\"text\")[0]", canonical);
        Assert.Equal(
            SyntaxDumper.Dump(original, DumperLimits),
            SyntaxDumper.Dump(reparsed, DumperLimits));
    }

    [Theory]
    [InlineData("1 ** (let x = 2; x)")]
    [InlineData("(let x = 1; x) + 2")]
    [InlineData("-(let x = 1; x)")]
    [InlineData("(1; 2) + 3")]
    [InlineData("-(1; 2)")]
    public void Sequence_like_operands_remain_parenthesized_in_precedence_contexts(string source)
    {
        SyntaxNode original = new SyntaxParser().Parse(source, ParserLimits).Root;

        string canonical = SyntaxPrinter.Print(original, PrinterLimits);
        SyntaxNode reparsed = new SyntaxParser().Parse(canonical, ParserLimits).Root;

        Assert.Equal(
            SyntaxDumper.Dump(original, DumperLimits),
            SyntaxDumper.Dump(reparsed, DumperLimits));
    }

    [Theory]
    [InlineData("(1; 2) ? 3 : 4")]
    [InlineData("true ? (1; 2) : 3")]
    [InlineData("true ? 3 : (1; 2)")]
    [InlineData("if (1; 2) { 3 } else { 4 }")]
    public void Sequence_conditions_and_ternary_branches_remain_parenthesized(string source)
    {
        SyntaxNode original = new SyntaxParser().Parse(source, ParserLimits).Root;

        string canonical = SyntaxPrinter.Print(original, PrinterLimits);
        SyntaxNode reparsed = new SyntaxParser().Parse(canonical, ParserLimits).Root;

        Assert.Equal(
            SyntaxDumper.Dump(original, DumperLimits),
            SyntaxDumper.Dump(reparsed, DumperLimits));
    }

    [Fact]
    public void Mutated_upstream_seeds_always_terminate_inside_parser_budgets()
    {
        string[] upstreamSeeds =
        [
            "!!!false",
            "!!(1 <= f64)",
            "!(\"bar\" not contains \"foo\")",
            "map(array, # > 0)",
            "filter(list, .Bar == \"bar\")",
            "all(array, #index >= 0)",
            "foo?.Bar",
            "1 in [1, 2, 3]",
            "let x = 1; x + 2",
            "if true { 1 } else { 2 }",
        ];

        foreach (ulong seed in Seeds)
        {
            var generator = new DeterministicExpressionGenerator(seed);
            for (var index = 0; index < 256; index++)
            {
                string source = upstreamSeeds[generator.NextInt(upstreamSeeds.Length)];
                string mutated = Mutate(source, generator);
                Assert.InRange(mutated.Length, 0, 512);
                AssertTerminatesAndStabilizesWhenValid(mutated);
            }
        }
    }

    [Fact]
    public void Ill_formed_utf16_and_replacement_decoded_utf8_terminate_deterministically()
    {
        string[] illFormedUtf16 =
        [
            "\ud800",
            "\udc00",
            "\"\ud800\"",
            "'\udc00'",
            "`left\ud800right`",
            "alpha\udc00beta",
            "// comment\ud800\n1",
            "b\"\ud800\"",
        ];
        foreach (string source in illFormedUtf16)
        {
            AssertTerminatesAndStabilizesWhenValid(source);
        }

        byte[][] illFormedUtf8 =
        [
            [0x80],
            [0xc0, 0xaf],
            [0xe2, 0x28, 0xa1],
            [0xf0, 0x28, 0x8c, 0xbc],
            [0xed, 0xa0, 0x80],
            [0xf5, 0x80, 0x80, 0x80],
            [0xe2, 0x82],
        ];
        var replacementDecoder = new UTF8Encoding(false, false);
        foreach (byte[] bytes in illFormedUtf8)
        {
            string decoded = replacementDecoder.GetString(bytes);
            Assert.Contains("\ufffd", decoded, StringComparison.Ordinal);
            AssertTerminatesAndStabilizesWhenValid(decoded);
            AssertTerminatesAndStabilizesWhenValid($"\"{decoded}\"");
            AssertTerminatesAndStabilizesWhenValid($"`{decoded}`");
        }
    }

    [Fact]
    public void Parser_rejects_depth_and_node_bombs_at_explicit_limits()
    {
        var options = new SyntaxParserOptions
        {
            MaximumNodeCount = 64,
            MaximumParseDepth = 32,
        };
        string deep = $"{new string('(', 256)}1{new string(')', 256)}";
        string wide = $"[{string.Join(',', Enumerable.Repeat("0", 256))}]";

        SyntaxException depthError = Assert.Throws<SyntaxException>(() => new SyntaxParser().Parse(deep, options));
        SyntaxException nodeError = Assert.Throws<SyntaxException>(() => new SyntaxParser().Parse(wide, options));

        Assert.Contains("maximum parse depth", depthError.Message, StringComparison.Ordinal);
        Assert.Contains("maximum allowed nodes", nodeError.Message, StringComparison.Ordinal);
    }

    private static string Mutate(string source, DeterministicExpressionGenerator generator)
    {
        int position = generator.NextInt(source.Length + 1);
        return generator.NextInt(7) switch
        {
            0 => source[..position],
            1 => source.Insert(position, generator.NextCodeUnit().ToString()),
            2 when source.Length > 0 => source.Remove(Math.Min(position, source.Length - 1), 1),
            3 => source.Insert(position, "/*"),
            4 => $"({source}",
            5 => $"\"{source}",
            _ => source + source[..position],
        };
    }

    private static void AssertTerminatesAndStabilizesWhenValid(string source)
    {
        var parser = new SyntaxParser();
        if (!parser.TryParse(source, out SyntaxTree? tree, out _, ParserLimits))
        {
            return;
        }

        Assert.NotNull(tree);
        string canonical = SyntaxPrinter.Print(tree.Root, PrinterLimits);
        SyntaxNode reparsed = new SyntaxParser().Parse(canonical, ParserLimits).Root;
        Assert.Equal(canonical, SyntaxPrinter.Print(reparsed, PrinterLimits));
    }
}
