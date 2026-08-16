using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Expr.Compilation;

namespace Expr.Execution;

/// <summary>Contains one immutable profile measurement.</summary>
/// <param name="Point">The compiler-defined profile point.</param>
/// <param name="Duration">The cumulative elapsed duration.</param>
/// <param name="InvocationCount">The number of completed measurements.</param>
public sealed record ExprProfileSample(
    ExprProfilePoint Point,
    TimeSpan Duration,
    long InvocationCount);

/// <summary>Contains an evaluation value and its resource-accounting details.</summary>
public sealed class ExprEvaluationResult
{
    internal ExprEvaluationResult(
        object? value,
        ulong memoryUsed,
        ulong workUsed,
        IEnumerable<ExprProfileSample> profile)
    {
        Value = value;
        MemoryUsed = memoryUsed;
        WorkUsed = workUsed;
        Profile = new ReadOnlyCollection<ExprProfileSample>(profile.ToArray());
    }

    /// <summary>Gets the expression result.</summary>
    public object? Value { get; }

    /// <summary>Gets the cumulative allocation charge.</summary>
    public ulong MemoryUsed { get; }

    /// <summary>Gets the number of executed instructions.</summary>
    public ulong WorkUsed { get; }

    /// <summary>Gets completed profile measurements in program point order.</summary>
    public IReadOnlyList<ExprProfileSample> Profile { get; }
}
