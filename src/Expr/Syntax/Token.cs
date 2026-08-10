using System;

namespace Expr.Syntax;

/// <summary>Identifies the lexical category of an Expr token.</summary>
public enum TokenKind
{
    /// <summary>An identifier.</summary>
    Identifier,
    /// <summary>A numeric literal.</summary>
    Number,
    /// <summary>A string literal.</summary>
    String,
    /// <summary>A byte-string literal.</summary>
    Bytes,
    /// <summary>An operator or punctuation token.</summary>
    Operator,
    /// <summary>An opening or closing bracket.</summary>
    Bracket,
    /// <summary>The end of the input.</summary>
    EndOfFile,
}

/// <summary>Represents one token produced by the Expr lexer.</summary>
public sealed record SyntaxToken
{
    private readonly byte[] bytesValue;

    /// <summary>Initializes a syntax token.</summary>
    /// <param name="kind">The token category.</param>
    /// <param name="value">The decoded token value.</param>
    /// <param name="location">The token source range.</param>
    /// <param name="bytesValue">Decoded bytes for a byte-string token.</param>
    public SyntaxToken(
        TokenKind kind,
        string value,
        SourceLocation location,
        ReadOnlyMemory<byte> bytesValue = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        Kind = kind;
        Value = value;
        Location = location;
        this.bytesValue = bytesValue.ToArray();
    }

    /// <summary>Gets the token category.</summary>
    public TokenKind Kind { get; }

    /// <summary>Gets the decoded token value.</summary>
    public string Value { get; }

    /// <summary>Gets the token source range.</summary>
    public SourceLocation Location { get; }

    /// <summary>Gets a defensive copy of decoded byte-string data.</summary>
    public ReadOnlyMemory<byte> BytesValue => (byte[])bytesValue.Clone();

    /// <summary>Determines whether the token has the requested category and optional value.</summary>
    /// <param name="kind">The requested category.</param>
    /// <param name="value">The requested value, or <see langword="null"/> to ignore value.</param>
    /// <returns><see langword="true"/> when the token matches.</returns>
    public bool Is(TokenKind kind, string? value = null) =>
        Kind == kind && (value is null || string.Equals(Value, value, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString()
    {
        var kind = Kind == TokenKind.EndOfFile ? "EOF" : Kind.ToString();
        if (Value.Length == 0)
        {
            return kind;
        }

        var escaped = Value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"{kind}(\"{escaped}\")";
    }
}
