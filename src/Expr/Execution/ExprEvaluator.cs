using System;
using System.Threading;
using Expr.Compilation;

namespace Expr.Execution;

/// <summary>Executes immutable Expr programs with isolated, thread-safe invocation state.</summary>
public sealed class ExprEvaluator
{
    /// <summary>Gets a shared stateless evaluator.</summary>
    public static ExprEvaluator Shared { get; } = new();

    /// <summary>Evaluates a compiled program.</summary>
    /// <param name="program">The immutable bytecode program.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="options">Optional resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The expression value.</returns>
    public object? Evaluate(
        ExprProgram program,
        object? environment = null,
        ExprEvaluationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        EvaluateDetailed(program, environment, options, cancellationToken).Value;

    /// <summary>Evaluates a program and returns resource and profiling information.</summary>
    /// <param name="program">The immutable bytecode program.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="options">Optional resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The value and evaluation measurements.</returns>
    public ExprEvaluationResult EvaluateDetailed(
        ExprProgram program,
        object? environment = null,
        ExprEvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        var requestedOptions = options ?? new ExprEvaluationOptions();
        ExprProgramValidator.Validate(program, requestedOptions);
        return new ExprExecutionMachine(program, environment, requestedOptions, cancellationToken).Run();
    }
}
