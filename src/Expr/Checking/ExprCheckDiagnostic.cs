using System;
using System.Globalization;
using Expr.Syntax;

namespace Expr.Checking;

/// <summary>Describes a static checking failure bound to expression source.</summary>
/// <param name="Message">The diagnostic message.</param>
/// <param name="Location">The source range.</param>
/// <param name="Line">The one-based line number.</param>
/// <param name="Column">The zero-based column number.</param>
/// <param name="Snippet">The formatted source snippet.</param>
public sealed record ExprCheckDiagnostic(
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

    internal static ExprCheckDiagnostic Create(string message, SyntaxNode node, SourceText source)
    {
        BoundSourceLocation bound = source.Bind(node.Location);
        return new ExprCheckDiagnostic(
            message,
            node.Location,
            bound.Line,
            bound.Column,
            bound.FormatSnippet());
    }
}

/// <summary>Represents an expression that is syntactically valid but fails static checking.</summary>
public sealed class ExprCheckException : Exception
{
    /// <summary>Initializes a checking exception without a bound source location.</summary>
    public ExprCheckException()
        : this("Expression checking failed.")
    {
    }

    /// <summary>Initializes a checking exception without a bound source location.</summary>
    /// <param name="message">The diagnostic message.</param>
    public ExprCheckException(string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Diagnostic = new ExprCheckDiagnostic(message, default, 0, 0, string.Empty);
    }

    /// <summary>Initializes a checking exception with an inner exception.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public ExprCheckException(string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(message);
        Diagnostic = new ExprCheckDiagnostic(message, default, 0, 0, string.Empty);
    }

    /// <summary>Initializes a checking exception from a structured diagnostic.</summary>
    /// <param name="diagnostic">The diagnostic.</param>
    public ExprCheckException(ExprCheckDiagnostic diagnostic)
        : base(diagnostic?.ToString())
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>Gets the structured diagnostic.</summary>
    public ExprCheckDiagnostic Diagnostic { get; }
}
