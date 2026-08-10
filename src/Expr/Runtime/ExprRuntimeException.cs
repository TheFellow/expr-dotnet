using System;

namespace Expr.Runtime;

/// <summary>
/// Represents an invalid Expr operation detected during evaluation.
/// </summary>
public sealed class ExprRuntimeException : Exception
{
    /// <summary>
    /// Initializes an Expr runtime exception.
    /// </summary>
    public ExprRuntimeException()
    {
    }

    /// <summary>
    /// Initializes an Expr runtime exception.
    /// </summary>
    /// <param name="message">The diagnostic message.</param>
    public ExprRuntimeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes an Expr runtime exception with an underlying exception.
    /// </summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public ExprRuntimeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
