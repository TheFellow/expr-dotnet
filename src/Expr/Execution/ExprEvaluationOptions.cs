using System;
using Expr.Configuration;

namespace Expr.Execution;

/// <summary>Configures one isolated Expr bytecode evaluation.</summary>
public sealed record ExprEvaluationOptions
{
    /// <summary>Gets the default instruction-work budget.</summary>
    public const ulong DefaultWorkBudget = 10_000_000;

    /// <summary>Gets or initializes the maximum cumulative allocation charge, or zero for no limit.</summary>
    public ulong MemoryBudget { get; init; } = ExprConfiguration.DefaultMemoryBudget;

    /// <summary>Gets or initializes the maximum number of executed instructions.</summary>
    public ulong WorkBudget
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfZero(value);
            field = value;
        }
    } = DefaultWorkBudget;

    /// <summary>Gets or initializes the maximum operand-stack depth.</summary>
    public int MaximumStackDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 65_536;

    /// <summary>Gets or initializes the maximum nested predicate-scope depth.</summary>
    public int MaximumScopeDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_024;

    /// <summary>Gets or initializes the maximum size of a collection created by bytecode.</summary>
    public int MaximumCollectionLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_000_000;

    /// <summary>Gets or initializes the maximum dynamic regular-expression pattern length.</summary>
    public int MaximumRegularExpressionLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 16_384;

    /// <summary>Gets or initializes the timeout for dynamic regular expressions.</summary>
    public TimeSpan RegularExpressionTimeout
    {
        get;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The regular-expression timeout must be positive.");
            }

            field = value;
        }
    } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or initializes whether profile boundary instructions collect timings.</summary>
    public bool EnableProfiling { get; init; }

    /// <summary>Creates evaluation options from compile-time resource settings.</summary>
    /// <param name="configuration">The compilation configuration.</param>
    /// <returns>Equivalent evaluation options.</returns>
    public static ExprEvaluationOptions FromConfiguration(ExprConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ExprEvaluationOptions
        {
            MemoryBudget = configuration.MemoryBudget,
            MaximumRegularExpressionLength = configuration.MaximumRegularExpressionLength,
            RegularExpressionTimeout = configuration.RegularExpressionTimeout,
        };
    }
}
