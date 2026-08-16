using System;
using System.Linq;
using System.Runtime.InteropServices;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Syntax;

public sealed class LexerTests
{
    [Fact]
    public void Lexes_upstream_token_corpus()
    {
        const string source = ".5 0.025 1 02 1e3 0xFF 0b0101 0o600 1.2e-4 1_000_000 _42 -.5";

        var actual = new SyntaxLexer().Lex(source).Select(token => (token.Kind, token.Value)).ToArray();

        Assert.Equal(
        [
            (TokenKind.Number, ".5"), (TokenKind.Number, "0.025"), (TokenKind.Number, "1"),
            (TokenKind.Number, "02"), (TokenKind.Number, "1e3"), (TokenKind.Number, "0xFF"),
            (TokenKind.Number, "0b0101"), (TokenKind.Number, "0o600"),
            (TokenKind.Number, "1.2e-4"), (TokenKind.Number, "1_000_000"),
            (TokenKind.Identifier, "_42"), (TokenKind.Operator, "-"), (TokenKind.Number, ".5"),
            (TokenKind.EndOfFile, string.Empty),
        ], actual);
    }

    [Fact]
    public void Decodes_strings_raw_strings_and_bytes()
    {
        const string source = "\"abc \\n\\t\\\"\\\\\" `hello\nworld` `a``b` b\"\\x00\\xff\" b'ÿ'";

        var tokens = new SyntaxLexer().Lex(source);

        Assert.Equal("abc \n\t\"\\", tokens[0].Value);
        Assert.Equal("hello\nworld", tokens[1].Value);
        Assert.Equal("a`b", tokens[2].Value);
        Assert.Equal(new byte[] { 0, 255 }, tokens[3].BytesValue.ToArray());
        Assert.Equal("ÿ"u8.ToArray(), tokens[4].BytesValue.ToArray());
    }

    [Fact]
    public void Skips_comments_and_recognizes_word_operators()
    {
        const string source = "foo // line\n not   in bar /* block */ and baz";

        var values = new SyntaxLexer().Lex(source).Select(token => token.Value).ToArray();

        Assert.Equal(["foo", "not", "in", "bar", "and", "baz", ""], values);
    }

    [Fact]
    public void Locations_are_unicode_scalar_offsets()
    {
        var tokens = new SyntaxLexer().Lex("früh == '✓'");

        Assert.Equal(new SourceLocation(0, 4), tokens[0].Location);
        Assert.Equal(new SourceLocation(5, 7), tokens[1].Location);
        Assert.Equal(new SourceLocation(8, 11), tokens[2].Location);
        Assert.Equal(new SourceLocation(10, 11), tokens[3].Location);
    }

    [Fact]
    public void Reports_bound_diagnostic_for_unterminated_literal()
    {
        var error = Assert.Throws<SyntaxException>(() => new SyntaxLexer().Lex("id \"hello"));

        Assert.Equal("literal not terminated", error.Diagnostic.Message);
        Assert.Equal(1, error.Diagnostic.Line);
        Assert.Contains("^", error.Diagnostic.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Can_treat_if_as_identifier()
    {
        var lexer = new SyntaxLexer { DisableIfOperator = true };

        var tokens = lexer.Lex("if(x) else");

        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[4].Kind);
    }

    [Theory]
    [InlineData("`backtick`", "backtick")]
    [InlineData("``", "")]
    [InlineData("`a``b`", "a`b")]
    [InlineData("````", "`")]
    [InlineData("`a```", "a`")]
    [InlineData("```b`", "`b")]
    [InlineData("```a````b```", "`a``b`")]
    [InlineData("````````", "```")]
    public void Decodes_upstream_raw_string_corpus(string source, string expected)
    {
        var token = new SyntaxLexer().Lex(source)[0];

        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("\"\\xQA\"", "invalid char escape")]
    [InlineData("\"hello", "literal not terminated")]
    [InlineData("`hello", "literal not terminated")]
    [InlineData("foo /* never closed", "unclosed comment")]
    [InlineData("b\"\\u0041\"", "unable to unescape string")]
    [InlineData("♥", "unrecognized character")]
    public void Rejects_upstream_lexer_error_corpus(string source, string expectedMessage)
    {
        var error = Assert.Throws<SyntaxException>(() => new SyntaxLexer().Lex(source));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Byte_token_does_not_expose_its_backing_storage()
    {
        var token = new SyntaxLexer().Lex("b'abc'")[0];
        var exposed = token.BytesValue;
        Assert.True(MemoryMarshal.TryGetArray(exposed, out var segment));

        segment.Array![segment.Offset] = (byte)'z';

        Assert.Equal("abc"u8.ToArray(), token.BytesValue.ToArray());
    }
}
