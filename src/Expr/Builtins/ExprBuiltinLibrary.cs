using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Expr.Runtime;
using Expr.Types;

namespace Expr.Builtins;

/// <summary>Provides the complete standard Expr function library.</summary>
public sealed class ExprBuiltinLibrary
{
    private readonly IReadOnlyDictionary<string, ExprFunction> functionsByName;

    /// <summary>Initializes a standard library with default deterministic services and limits.</summary>
    public ExprBuiltinLibrary()
        : this(ExprBuiltinOptions.Default)
    {
    }

    /// <summary>Initializes a standard library.</summary>
    /// <param name="options">Clock, timezone, and resource settings.</param>
    public ExprBuiltinLibrary(ExprBuiltinOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        IReadOnlyList<ExprFunction> functions = ExprBuiltinDefinitions.Create(this);
        Functions = functions;
        functionsByName = new ReadOnlyDictionary<string, ExprFunction>(
            functions.ToDictionary(static function => function.Name, StringComparer.Ordinal));
        Names = Array.AsReadOnly(functions.Select(static function => function.Name).ToArray());
    }

    /// <summary>Gets the default standard library.</summary>
    public static ExprBuiltinLibrary Standard { get; } = new();

    /// <summary>Gets the configured services and resource limits.</summary>
    public ExprBuiltinOptions Options { get; }

    /// <summary>Gets all built-ins in their canonical upstream order.</summary>
    public IReadOnlyList<ExprFunction> Functions { get; }

    /// <summary>Gets all expression-visible built-in names in canonical upstream order.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Gets a built-in by its ordinal, case-sensitive name.</summary>
    /// <param name="name">The expression-visible name.</param>
    /// <returns>The function metadata and direct invoker.</returns>
    /// <exception cref="KeyNotFoundException">The name is not a standard built-in.</exception>
    public ExprFunction Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return functionsByName.TryGetValue(name, out ExprFunction? function)
            ? function
            : throw new KeyNotFoundException($"unknown builtin {name}");
    }

    /// <summary>Attempts to find a built-in by its ordinal, case-sensitive name.</summary>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="function">The matching function.</param>
    /// <returns><see langword="true"/> when the built-in exists.</returns>
    public bool TryGet(string name, out ExprFunction? function)
    {
        ArgumentNullException.ThrowIfNull(name);
        return functionsByName.TryGetValue(name, out function);
    }

    /// <summary>Executes a predicate built-in with VM-supplied predicate state.</summary>
    /// <param name="name">The predicate built-in name.</param>
    /// <param name="collection">The input collection.</param>
    /// <param name="context">The predicate, accumulator, and ordering state.</param>
    /// <returns>The result and its resource charge.</returns>
    public ExprInvocationResult InvokePredicate(
        string name,
        object? collection,
        ExprBuiltinPredicateContext context)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(context);
        return ExprBuiltinPredicates.Invoke(name, collection, context, Options);
    }

    internal static ExprFunctionOverload Overload(
        ExprTypeDescriptor result,
        bool variadic,
        params ExprTypeDescriptor[] parameters) => new(parameters, result, variadic);
}
