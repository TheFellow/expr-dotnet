using System;
using System.Threading;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Syntax;

namespace Expr;

/// <summary>Contains an immutable, reusable Expr compilation and its inspectable intermediate representations.</summary>
/// <remarks>
/// Instances are safe to evaluate concurrently. Each evaluation uses isolated virtual-machine state;
/// thread safety of the supplied environment and host functions remains the caller's responsibility.
/// </remarks>
public sealed class CompiledExpression
{
    private readonly ExprEvaluationOptions defaultEvaluationOptions;

    internal CompiledExpression(
        ExprSemanticModel semanticModel,
        ExprProgram program,
        ExprConfiguration configuration,
        ExprCompilationOptions compilationOptions)
    {
        SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        Program = program ?? throw new ArgumentNullException(nameof(program));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ArgumentNullException.ThrowIfNull(compilationOptions);
        defaultEvaluationOptions = ExprEvaluationOptions.FromConfiguration(configuration) with
        {
            EnableProfiling = compilationOptions.EnableProfiling,
        };
    }

    /// <summary>Gets the checked and, when enabled, optimized syntax tree.</summary>
    public SyntaxTree SyntaxTree => SemanticModel.SyntaxTree;

    /// <summary>Gets the semantic annotations over <see cref="SyntaxTree"/>.</summary>
    public ExprSemanticModel SemanticModel { get; }

    /// <summary>Gets the immutable virtual-machine program.</summary>
    public ExprProgram Program { get; }

    /// <summary>Gets the immutable configuration used to produce this compilation.</summary>
    public ExprConfiguration Configuration { get; }

    /// <summary>Evaluates the expression with isolated invocation state.</summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="options">Optional per-invocation resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The expression result.</returns>
    public object? Run(
        object? environment = null,
        ExprEvaluationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExprEvaluator.Shared.Evaluate(
            Program,
            environment,
            options ?? defaultEvaluationOptions,
            cancellationToken);

    /// <summary>Evaluates the expression and returns resource and profiling measurements.</summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="options">Optional per-invocation resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The expression result with evaluation measurements.</returns>
    public ExprEvaluationResult RunDetailed(
        object? environment = null,
        ExprEvaluationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExprEvaluator.Shared.EvaluateDetailed(
            Program,
            environment,
            options ?? defaultEvaluationOptions,
            cancellationToken);
}
