using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Expr.Syntax;

/// <summary>Tokenizes Expr source text.</summary>
public sealed class SyntaxLexer
{
    private readonly List<SyntaxToken> tokens = [];
    private SourceText source = new(string.Empty);
    private Rune[] runes = [];
    private int[] offsets = [0];
    private int position;
    private int start;

    /// <summary>Gets or sets whether <c>if</c> and <c>else</c> are emitted as identifiers.</summary>
    public bool DisableIfOperator { get; set; }

    /// <summary>Tokenizes an expression and includes an end-of-file token.</summary>
    /// <param name="text">The expression source.</param>
    /// <returns>The tokens in source order.</returns>
    /// <exception cref="SyntaxException">The input contains an invalid token or literal.</exception>
    public IReadOnlyList<SyntaxToken> Lex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        source = new SourceText(text);
        runes = [.. text.EnumerateRunes()];
        offsets = BuildOffsets(runes);
        tokens.Clear();
        position = 0;
        start = 0;

        while (position < runes.Length)
        {
            start = position;
            LexToken();
        }

        var eofStart = Math.Max(0, position - 1);
        tokens.Add(new SyntaxToken(TokenKind.EndOfFile, string.Empty, new SourceLocation(eofStart, position)));
        return tokens.ToArray();
    }

    private static int[] BuildOffsets(IReadOnlyList<Rune> values)
    {
        var result = new int[values.Count + 1];
        for (var index = 0; index < values.Count; index++)
        {
            result[index + 1] = result[index] + values[index].Utf16SequenceLength;
        }

        return result;
    }

    private void LexToken()
    {
        var current = Advance();
        if (Rune.IsWhiteSpace(current))
        {
            return;
        }

        if (current.Value is '\'' or '"')
        {
            ScanQuoted(current);
            Emit(TokenKind.String, Unescape(Raw(), bytes: false).Text);
            return;
        }

        if (current.Value == '`')
        {
            ScanRaw();
            return;
        }

        if ((current.Value is 'b' or 'B') && Peek().Value is '\'' or '"')
        {
            var quote = Advance();
            ScanQuoted(quote);
            var decoded = Unescape(Raw()[1..], bytes: true);
            Emit(TokenKind.Bytes, Encoding.Latin1.GetString(decoded.Bytes), decoded.Bytes);
            return;
        }

        if (IsAsciiDigit(current))
        {
            position--;
            ScanNumber();
            return;
        }

        switch (current.Value)
        {
            case '?':
                if (Peek().Value is '?' or '.')
                {
                    position++;
                }

                Emit(TokenKind.Operator);
                return;
            case '/':
                if (Accept('/'))
                {
                    while (position < runes.Length && Peek().Value != '\n')
                    {
                        position++;
                    }

                    return;
                }

                if (Accept('*'))
                {
                    while (position < runes.Length)
                    {
                        if (Advance().Value == '*' && Accept('/'))
                        {
                            return;
                        }
                    }

                    Fail("unclosed comment");
                    return;
                }

                Emit(TokenKind.Operator);
                return;
            case '#':
                Emit(TokenKind.Operator);
                start = position;
                while (IsAlphaNumeric(Peek()))
                {
                    position++;
                }

                if (position > start)
                {
                    Emit(TokenKind.Identifier);
                }

                return;
            case '|':
                Accept('|');
                Emit(TokenKind.Operator);
                return;
            case ':':
                Accept(':');
                Emit(TokenKind.Operator);
                return;
            case '(' or ')' or '[' or ']' or '{' or '}':
                Emit(TokenKind.Bracket);
                return;
            case ',' or ';' or '%' or '+' or '-' or '^':
                Emit(TokenKind.Operator);
                return;
            case '&' or '!' or '=' or '*' or '<' or '>':
                if (Peek().Value is '&' or '=' or '*')
                {
                    position++;
                }

                Emit(TokenKind.Operator);
                return;
            case '.':
                if (IsAsciiDigit(Peek()))
                {
                    position--;
                    ScanNumber();
                    return;
                }

                Accept('.');
                Emit(TokenKind.Operator);
                return;
        }

        if (IsAlphaNumeric(current))
        {
            ScanIdentifier();
            return;
        }

        Fail($"unrecognized character: U+{current.Value:X4} '{current}'");
    }

    private void ScanIdentifier()
    {
        while (IsAlphaNumeric(Peek()))
        {
            position++;
        }

        var word = Raw();
        if (word == "not")
        {
            Emit(TokenKind.Operator);
            var saved = position;
            while (Peek().Value == ' ')
            {
                position++;
            }

            start = position;
            while (IsAlphaNumeric(Peek()))
            {
                position++;
            }

            var suffix = Raw();
            if (suffix is "in" or "matches" or "contains" or "startsWith" or "endsWith")
            {
                Emit(TokenKind.Operator);
            }
            else
            {
                position = saved;
                start = saved;
            }

            return;
        }

        var isOperator = word is "in" or "or" or "and" or "matches" or "contains" or
            "startsWith" or "endsWith" or "let" ||
            (!DisableIfOperator && word is "if" or "else");
        Emit(isOperator ? TokenKind.Operator : TokenKind.Identifier, word);
    }

    private void ScanNumber()
    {
        var digits = "0123456789_";
        if (Accept('0'))
        {
            if (Accept('x') || Accept('X'))
            {
                digits = "0123456789abcdefABCDEF_";
            }
            else if (Accept('o') || Accept('O'))
            {
                digits = "01234567_";
            }
            else if (Accept('b') || Accept('B'))
            {
                digits = "01_";
            }
        }

        AcceptRun(digits);
        var integralEnd = position;
        if (Accept('.'))
        {
            if (Peek().Value == '.')
            {
                position = integralEnd;
                Emit(TokenKind.Number);
                return;
            }

            AcceptRun(digits);
        }

        if (Accept('e') || Accept('E'))
        {
            _ = Accept('+') || Accept('-');
            AcceptRun(digits);
        }

        if (IsAlphaNumeric(Peek()))
        {
            position++;
            Fail($"bad number syntax: \"{Raw()}\"");
        }

        Emit(TokenKind.Number);
    }

    private void ScanQuoted(Rune quote)
    {
        while (position < runes.Length)
        {
            var value = Advance();
            if (value == quote)
            {
                return;
            }

            if (value.Value == '\n')
            {
                Fail("literal not terminated");
            }

            if (value.Value == '\\')
            {
                ValidateEscape(quote);
            }
        }

        Fail("literal not terminated", position);
    }

    private void ScanRaw()
    {
        var builder = new StringBuilder();
        while (position < runes.Length)
        {
            var value = Advance();
            if (value.Value != '`')
            {
                builder.Append(value.ToString());
                continue;
            }

            if (Accept('`'))
            {
                builder.Append('`');
                continue;
            }

            Emit(TokenKind.String, builder.ToString());
            return;
        }

        Fail("literal not terminated", position);
    }

    private void ValidateEscape(Rune quote)
    {
        if (position >= runes.Length)
        {
            Fail("invalid char escape", position);
        }

        var escape = Advance().Value;
        if (escape is 'a' or 'b' or 'f' or 'n' or 'r' or 't' or 'v' or '\\' || escape == quote.Value)
        {
            return;
        }

        if (escape is >= '0' and <= '7')
        {
            ConsumeHexDigits(8, 2);
            return;
        }

        if (escape == 'x')
        {
            ConsumeHexDigits(16, 2);
            return;
        }

        if (escape == 'u' && Accept('{'))
        {
            var digits = 0;
            while (position < runes.Length && Peek().Value != '}')
            {
                if (HexValue(Peek().Value) >= 16 || digits == 6)
                {
                    Fail("invalid char escape");
                }

                position++;
                digits++;
            }

            if (digits == 0 || !Accept('}'))
            {
                Fail("invalid char escape");
            }

            return;
        }

        if (escape == 'u')
        {
            ConsumeHexDigits(16, 4);
            return;
        }

        if (escape == 'U')
        {
            ConsumeHexDigits(16, 8);
            return;
        }

        Fail("invalid char escape");
    }

    private (string Text, byte[] Bytes) Unescape(string quoted, bool bytes)
    {
        var content = quoted.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        content = content[1..^1];
        var result = new StringBuilder();
        var bytesResult = new List<byte>();
        var contentRunes = content.EnumerateRunes().ToArray();
        for (var index = 0; index < contentRunes.Length; index++)
        {
            var value = contentRunes[index];
            if (value.Value != '\\')
            {
                Append(value, bytes, result, bytesResult);
                continue;
            }

            if (++index >= contentRunes.Length)
            {
                Fail("unable to unescape string");
            }

            var escape = contentRunes[index].Value;
            var decoded = Rune.ReplacementChar;
            switch (escape)
            {
                case 'a': decoded = new Rune('\a'); break;
                case 'b': decoded = new Rune('\b'); break;
                case 'f': decoded = new Rune('\f'); break;
                case 'n': decoded = new Rune('\n'); break;
                case 'r': decoded = new Rune('\r'); break;
                case 't': decoded = new Rune('\t'); break;
                case 'v': decoded = new Rune('\v'); break;
                case '\\': decoded = new Rune('\\'); break;
                case '\'': decoded = new Rune('\''); break;
                case '"': decoded = new Rune('"'); break;
                case '`': decoded = new Rune('`'); break;
                case '?': decoded = new Rune('?'); break;
                case 'x' or 'X':
                    decoded = new Rune(ReadEscape(contentRunes, ref index, 2, 16));
                    break;
                case >= '0' and <= '3':
                    decoded = new Rune(((escape - '0') << 6) | ReadEscape(contentRunes, ref index, 2, 8));
                    break;
                case 'u' when bytes:
                case 'U' when bytes:
                    Fail("unable to unescape string");
                    return default;
                case 'u' when index + 1 < contentRunes.Length && contentRunes[index + 1].Value == '{':
                    index += 2;
                    var scalar = 0;
                    var count = 0;
                    while (index < contentRunes.Length && contentRunes[index].Value != '}')
                    {
                        scalar = checked((scalar << 4) | HexValue(contentRunes[index].Value));
                        count++;
                        index++;
                    }

                    if (count is < 1 or > 6 || index >= contentRunes.Length || !Rune.TryCreate(scalar, out decoded))
                    {
                        Fail("unable to unescape string");
                    }

                    break;
                case 'u':
                    var unicode = ReadEscape(contentRunes, ref index, 4, 16);
                    if (!Rune.TryCreate(unicode, out decoded))
                    {
                        Fail("unable to unescape string");
                    }

                    break;
                case 'U':
                    var longUnicode = ReadEscape(contentRunes, ref index, 8, 16);
                    if (!Rune.TryCreate(longUnicode, out decoded))
                    {
                        Fail("unable to unescape string");
                    }

                    break;
                default:
                    Fail("unable to unescape string");
                    return default;
            }

            Append(decoded, bytes, result, bytesResult, rawByte: bytes && escape is 'x' or 'X' or >= '0' and <= '3');
        }

        return (result.ToString(), bytesResult.ToArray());
    }

    private static void Append(
        Rune value,
        bool bytes,
        StringBuilder text,
        List<byte> result,
        bool rawByte = false)
    {
        text.Append(value.ToString());
        if (!bytes)
        {
            return;
        }

        if (rawByte)
        {
            result.Add((byte)value.Value);
            return;
        }

        Span<byte> encoded = stackalloc byte[4];
        var length = value.EncodeToUtf8(encoded);
        for (var index = 0; index < length; index++)
        {
            result.Add(encoded[index]);
        }
    }

    private void ConsumeHexDigits(int numberBase, int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (position >= runes.Length || HexValue(Advance().Value) >= numberBase)
            {
                Fail("invalid char escape");
            }
        }
    }

    private void AcceptRun(string values)
    {
        while (position < runes.Length && values.Contains((char)Peek().Value, StringComparison.Ordinal))
        {
            position++;
        }
    }

    private bool Accept(int value)
    {
        if (Peek().Value != value)
        {
            return false;
        }

        position++;
        return true;
    }

    private Rune Advance() => runes[position++];

    private Rune Peek() => position < runes.Length ? runes[position] : new Rune(0);

    private string Raw() => source.Text[offsets[start]..offsets[position]];

    private void Emit(TokenKind kind) => Emit(kind, Raw());

    private void Emit(TokenKind kind, string value, byte[]? valueBytes = null)
    {
        tokens.Add(new SyntaxToken(kind, value, new SourceLocation(start, position), valueBytes));
        start = position;
    }

    private void Fail(string message, int? errorPosition = null)
    {
        var at = errorPosition ?? Math.Max(start, position - 1);
        var location = new SourceLocation(at, at + 1);
        var bound = source.Bind(location);
        throw new SyntaxException(new SyntaxDiagnostic(message, location, bound.Line, bound.Column, bound.FormatSnippet()));
    }

    private static int ReadEscape(IReadOnlyList<Rune> values, ref int index, int count, int numberBase)
    {
        var result = 0;
        for (var digit = 0; digit < count; digit++)
        {
            index++;
            if (index >= values.Count)
            {
                return -1;
            }

            var value = HexValue(values[index].Value);
            if (value >= numberBase)
            {
                return -1;
            }

            result = (result * numberBase) + value;
        }

        return result;
    }

    private static int HexValue(int value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => 16,
    };

    private static bool IsAsciiDigit(Rune value) => value.Value is >= '0' and <= '9';

    private static bool IsAlphaNumeric(Rune value) =>
        value.Value is '_' or '$' || Rune.IsLetter(value) || Rune.IsDigit(value);
}
