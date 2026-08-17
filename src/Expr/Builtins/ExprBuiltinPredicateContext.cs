using System;

namespace Expr.Builtins;

/// <summary>Evaluates a compiler-supplied predicate for a collection item.</summary>
/// <param name="value">The current item.</param>
/// <param name="index">The zero-based item index.</param>
/// <param name="accumulator">The current reduce accumulator, or <see langword="null"/>.</param>
/// <returns>The predicate or projection result.</returns>
public delegate object? ExprBuiltinPredicate(object? value, int index, object? accumulator);

/// <summary>Supplies the VM-owned state required by a predicate built-in.</summary>
public sealed record ExprBuiltinPredicateContext
{
    /// <summary>Initializes predicate execution state.</summary>
    /// <param name="predicate">The predicate or projection.</param>
    public ExprBuiltinPredicateContext(ExprBuiltinPredicate predicate)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <summary>Gets the predicate or projection.</summary>
    public ExprBuiltinPredicate Predicate { get; }

    /// <summary>Gets the explicit initial reduce value.</summary>
    public object? InitialValue { get; init; }

    /// <summary>Gets whether <see cref="InitialValue"/> was explicitly supplied.</summary>
    public bool HasInitialValue { get; init; }

    /// <summary>Gets the requested sort order for <c>sortBy</c>.</summary>
    public string SortOrder
    {
        get;
        init
        {
            if (value is not ("asc" or "desc"))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Sort order must be asc or desc.");
            }

            field = value;
        }
    } = "asc";
}
