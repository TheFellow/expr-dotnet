using System;
using Expr.Syntax;

namespace Expr.Optimization;

/// <summary>Represents a failure encountered while evaluating a compile-time expression.</summary>
public sealed class ExprOptimizationException : Exception
{
    /// <summary>Initializes an optimization exception.</summary>
    public ExprOptimizationException()
        : this("Expression optimization failed.")
    {
    }

    /// <summary>Initializes an optimization exception without a bound source location.</summary>
    /// <param name="message">The failure message.</param>
    public ExprOptimizationException(string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(message);
    }

    /// <summary>Initializes an optimization exception with an inner exception.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The exception raised by compile-time evaluation.</param>
    public ExprOptimizationException(string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(innerException);
    }

    /// <summary>Initializes an optimization exception at a source location.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="location">The expression source location.</param>
    /// <param name="innerException">The optional exception raised by compile-time evaluation.</param>
    public ExprOptimizationException(string message, SourceLocation location, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(message);
        Location = location;
    }

    /// <summary>Gets the source location associated with the failure.</summary>
    public SourceLocation Location { get; }
}
