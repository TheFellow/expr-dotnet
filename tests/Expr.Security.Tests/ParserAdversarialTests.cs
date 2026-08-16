using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Expr.Configuration;
using Expr.Syntax;
using Xunit;

namespace Expr.Security.Tests;

public sealed class ParserAdversarialTests
{
    public static TheoryData<string> MalformedSources =>
    [
        "'\\uD800'",
        "'\\U00110000'",
        "'\\u{}'",
        "'\\xGG'",
        "1abc",
        "1e999999",
        "0xFFFFFFFFFFFFFFFFF",
        "/* unterminated",
    ];

    public static TheoryData<string> OversizedSources =>
    [
        new string(' ', 65),
        string.Concat("/*", new string('x', 65), "*/"),
        new string('a', 65),
        string.Concat("'", new string('x', 65), "'"),
    ];

    [Theory]
    [MemberData(nameof(OversizedSources))]
    public void Oversized_whitespace_comments_identifiers_and_strings_are_rejected_before_lexing(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var options = new SyntaxParserOptions { MaximumSourceLength = 32 };

        SyntaxException exception = Assert.Throws<SyntaxException>(() =>
            new SyntaxParser().Parse(source, options));

        Assert.Contains("maximum source length of 32", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Facade_configuration_applies_the_source_length_limit()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithMaximumSourceLength(3);

        SyntaxException exception = Assert.Throws<SyntaxException>(() =>
            ExprEngine.Parse("true", configuration));

        Assert.Contains("maximum source length of 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deep_parentheses_are_rejected_at_the_configured_parse_depth()
    {
        const int nesting = 256;
        string source = string.Concat(Enumerable.Repeat("(", nesting)) + "1" +
            string.Concat(Enumerable.Repeat(")", nesting));
        var options = new SyntaxParserOptions { MaximumParseDepth = 32 };

        SyntaxException exception = Assert.Throws<SyntaxException>(() => new SyntaxParser().Parse(source, options));

        Assert.Contains("maximum parse depth of 32", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wide_array_is_rejected_at_the_configured_node_limit()
    {
        string source = "[" + string.Join(",", Enumerable.Repeat("0", 2_048)) + "]";
        var options = new SyntaxParserOptions
        {
            MaximumNodeCount = 128,
            MaximumParseDepth = 64,
        };

        SyntaxException exception = Assert.Throws<SyntaxException>(() => new SyntaxParser().Parse(source, options));

        Assert.Contains("maximum allowed nodes", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedSources))]
    public void Malformed_escapes_numbers_and_comments_return_structured_diagnostics(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool parsed = new SyntaxParser().TryParse(source, out SyntaxTree? tree, out SyntaxDiagnostic? diagnostic);

        Assert.False(parsed);
        Assert.Null(tree);
        Assert.NotNull(diagnostic);
        Assert.InRange(diagnostic.Location.Start, 0, source.EnumerateRunes().Count());
        Assert.True(diagnostic.Line >= 1);
        Assert.NotEmpty(diagnostic.Message);
    }

    [Fact]
    public void Ill_formed_utf16_is_rejected_without_an_unhandled_exception()
    {
        string source = string.Create(1, '\ud800', static (destination, value) => destination[0] = value);

        bool parsed = new SyntaxParser().TryParse(source, out SyntaxTree? tree, out SyntaxDiagnostic? diagnostic);

        Assert.False(parsed);
        Assert.Null(tree);
        Assert.NotNull(diagnostic);
        Assert.Contains("unrecognized character", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_state_is_reset_after_a_rejected_expression()
    {
        var parser = new SyntaxParser();
        Assert.False(parser.TryParse("[1,", out _, out _));

        SyntaxTree tree = parser.Parse("1 + 2");

        Assert.IsType<BinaryNode>(tree.Root);
    }

    [Fact]
    public void Independent_parsers_and_shared_immutable_trees_are_safe_under_concurrency()
    {
        SyntaxTree shared = new SyntaxParser().Parse("users | filter(.active && #index >= 0)");

        Parallel.For(
            0,
            256,
            iteration =>
            {
                SyntaxTree parsed = new SyntaxParser().Parse($"[{iteration}, {iteration + 1}, {iteration + 2}]");
                _ = SyntaxWalker.Find(parsed.Root, static node => node is IntegerNode);
                _ = SyntaxWalker.Find(shared.Root, static node => node is PredicateNode);
            });
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void Numeric_parsing_is_invariant_under_host_culture(string cultureName)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            var value = Assert.IsType<FloatNode>(new SyntaxParser().Parse("1234.5").Root);

            Assert.Equal(1234.5D, value.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
