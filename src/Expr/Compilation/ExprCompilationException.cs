using System;
using Expr.Syntax;

namespace Expr.Compilation;

/// <summary>Represents a failure while lowering checked syntax to bytecode.</summary>
public sealed class ExprCompilationException : Exception
{
    /// <summary>Initializes a compilation exception.</summary>
    public ExprCompilationException()
    {
    }

    /// <summary>Initializes a compilation exception.</summary>
    /// <param name="message">The diagnostic message.</param>
    public ExprCompilationException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a compilation exception with an underlying failure.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ExprCompilationException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a compilation exception.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="location">The related source range.</param>
    public ExprCompilationException(string message, SourceLocation location)
        : base(message) => Location = location;

    /// <summary>Initializes a compilation exception with an underlying failure.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="location">The related source range.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ExprCompilationException(string message, SourceLocation location, Exception innerException)
        : base(message, innerException) => Location = location;

    /// <summary>Gets the source range associated with the failure.</summary>
    public SourceLocation Location { get; }
}
