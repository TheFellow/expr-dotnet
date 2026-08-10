using System;
using System.Globalization;

namespace Expr.Syntax;

/// <summary>Describes a lexer or parser error.</summary>
/// <param name="Message">The diagnostic message.</param>
/// <param name="Location">The source range associated with the error.</param>
/// <param name="Line">The one-based line number.</param>
/// <param name="Column">The zero-based column number.</param>
/// <param name="Snippet">A formatted source snippet.</param>
public sealed record SyntaxDiagnostic(
    string Message,
    SourceLocation Location,
    int Line,
    int Column,
    string Snippet)
{
    /// <inheritdoc />
    public override string ToString() => Snippet.Length == 0
        ? Message
        : string.Create(CultureInfo.InvariantCulture, $"{Message} ({Line}:{Column + 1}){Snippet}");
}

/// <summary>Represents a failure to lex or parse an expression.</summary>
public sealed class SyntaxException : Exception
{
    /// <summary>Initializes an exception without a structured source location.</summary>
    public SyntaxException()
        : this(CreateUnboundDiagnostic("A syntax error occurred."))
    {
    }

    /// <summary>Initializes an exception without a structured source location.</summary>
    /// <param name="message">The error message.</param>
    public SyntaxException(string message)
        : this(CreateUnboundDiagnostic(message))
    {
    }

    /// <summary>Initializes an exception with an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public SyntaxException(string message, Exception innerException)
        : base(message, innerException)
    {
        Diagnostic = CreateUnboundDiagnostic(message);
    }

    /// <summary>Initializes an exception for the supplied diagnostic.</summary>
    /// <param name="diagnostic">The syntax diagnostic.</param>
    public SyntaxException(SyntaxDiagnostic diagnostic)
        : base(diagnostic?.ToString())
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>Gets the structured diagnostic.</summary>
    public SyntaxDiagnostic Diagnostic { get; }

    private static SyntaxDiagnostic CreateUnboundDiagnostic(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new SyntaxDiagnostic(message, default, 0, 0, string.Empty);
    }
}
