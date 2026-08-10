using System;
using System.Globalization;
using Expr.Syntax;

namespace Expr.Execution;

/// <summary>Represents a bytecode validation or evaluation failure bound to Expr source.</summary>
public sealed class ExprExecutionException : Exception
{
    /// <summary>Initializes an empty execution failure.</summary>
    public ExprExecutionException()
    {
    }

    /// <summary>Initializes an execution failure.</summary>
    /// <param name="message">The failure message.</param>
    public ExprExecutionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an execution failure with an underlying failure.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ExprExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a source-bound execution failure.</summary>
    /// <param name="message">The stable failure message.</param>
    /// <param name="instructionIndex">The zero-based instruction index, or <c>-1</c> before execution.</param>
    /// <param name="location">The associated source range.</param>
    /// <param name="source">The program source.</param>
    /// <param name="innerException">The underlying host failure, when present.</param>
    public ExprExecutionException(
        string message,
        int instructionIndex,
        SourceLocation location,
        SourceText source,
        Exception? innerException = null)
        : base(FormatMessage(message, location, source), innerException)
    {
        ArgumentNullException.ThrowIfNull(source);
        InstructionIndex = instructionIndex;
        Location = location;
    }

    /// <summary>Gets the instruction that failed, or <c>-1</c> for program-level validation.</summary>
    public int InstructionIndex { get; }

    /// <summary>Gets the source range associated with the failure.</summary>
    public SourceLocation Location { get; }

    private static string FormatMessage(string message, SourceLocation location, SourceText source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(source);
        BoundSourceLocation bound = source.Bind(location);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{message} ({bound.Line}:{bound.Column + 1}){bound.FormatSnippet()}");
    }
}
