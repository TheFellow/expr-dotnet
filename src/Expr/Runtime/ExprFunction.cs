using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Types;

namespace Expr.Runtime;

/// <summary>Invokes an Expr function using an allocation-free view of its arguments.</summary>
/// <param name="arguments">The ordered arguments.</param>
/// <returns>The function result.</returns>
public delegate object? ExprFunctionInvoker(ReadOnlySpan<object?> arguments);

/// <summary>Invokes a resource-accounting Expr function.</summary>
/// <param name="arguments">The ordered arguments.</param>
/// <returns>The result and memory cost reported by the function.</returns>
public delegate ExprInvocationResult ExprSafeFunctionInvoker(ReadOnlySpan<object?> arguments);

/// <summary>Estimates an upper bound for memory allocated by an Expr function before it runs.</summary>
/// <param name="arguments">The ordered arguments.</param>
/// <returns>The allocation charge upper bound.</returns>
public delegate ulong ExprFunctionMemoryEstimator(ReadOnlySpan<object?> arguments);

/// <summary>Validates argument types and computes a function result type.</summary>
/// <param name="arguments">The ordered argument types.</param>
/// <returns>The result type.</returns>
public delegate ExprTypeDescriptor ExprFunctionTypeValidator(ReadOnlySpan<ExprTypeDescriptor> arguments);

/// <summary>Contains the value and resource charge returned by a safe function.</summary>
/// <param name="Value">The returned value.</param>
/// <param name="MemoryCost">The number of memory-budget units consumed.</param>
public readonly record struct ExprInvocationResult(object? Value, ulong MemoryCost);

/// <summary>Declares one statically checkable function overload.</summary>
public sealed record ExprFunctionOverload
{
    /// <summary>
    /// Initializes a function overload.
    /// </summary>
    /// <param name="parameters">The ordered parameter types.</param>
    /// <param name="returnType">The return type.</param>
    /// <param name="isVariadic">Whether the final parameter may repeat.</param>
    public ExprFunctionOverload(
        IEnumerable<ExprTypeDescriptor> parameters,
        ExprTypeDescriptor returnType,
        bool isVariadic = false)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Parameters = Array.AsReadOnly(parameters.ToArray());
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        IsVariadic = isVariadic;
        if (isVariadic && Parameters.Count is 0)
        {
            throw new ArgumentException("A variadic overload requires at least one parameter.", nameof(parameters));
        }
    }

    /// <summary>Gets the ordered parameter types.</summary>
    public IReadOnlyList<ExprTypeDescriptor> Parameters { get; }

    /// <summary>Gets the return type.</summary>
    public ExprTypeDescriptor ReturnType { get; }

    /// <summary>Gets a value indicating whether the final parameter may repeat.</summary>
    public bool IsVariadic { get; }

    /// <summary>Determines whether an argument count can be accepted.</summary>
    /// <param name="argumentCount">The supplied argument count.</param>
    /// <returns><see langword="true"/> when the arity is valid.</returns>
    public bool AcceptsArity(int argumentCount) => IsVariadic
        ? argumentCount >= Parameters.Count - 1
        : argumentCount == Parameters.Count;
}

/// <summary>
/// Describes a named Expr function, its overloads, and its safe runtime invocation contract.
/// </summary>
public sealed class ExprFunction
{
    /// <summary>
    /// Initializes a function declaration.
    /// </summary>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="overloads">The statically checkable overloads.</param>
    /// <param name="invoker">The ordinary invoker.</param>
    /// <param name="safeInvoker">The resource-accounting invoker.</param>
    /// <param name="typeValidator">An optional custom type validator.</param>
    /// <param name="isPredicate">Whether the compiler supplies a predicate closure.</param>
    /// <param name="memoryEstimator">An optional pre-invocation allocation upper bound.</param>
    public ExprFunction(
        string name,
        IEnumerable<ExprFunctionOverload> overloads,
        ExprFunctionInvoker? invoker = null,
        ExprSafeFunctionInvoker? safeInvoker = null,
        ExprFunctionTypeValidator? typeValidator = null,
        bool isPredicate = false,
        ExprFunctionMemoryEstimator? memoryEstimator = null)
        : this(
            name,
            overloads,
            invoker,
            safeInvoker,
            typeValidator,
            isPredicate,
            memoryEstimator,
            enforceRuntimeArity: true)
    {
    }

    internal ExprFunction(
        string name,
        IEnumerable<ExprFunctionOverload> overloads,
        ExprFunctionInvoker? invoker,
        ExprSafeFunctionInvoker? safeInvoker,
        ExprFunctionTypeValidator? typeValidator,
        bool isPredicate,
        ExprFunctionMemoryEstimator? memoryEstimator,
        bool enforceRuntimeArity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(overloads);
        ExprFunctionOverload[] overloadSnapshot = [.. overloads];
        if (overloadSnapshot.Length is 0 && typeValidator is null)
        {
            throw new ArgumentException("At least one overload or a type validator is required.", nameof(overloads));
        }

        if (invoker is null && safeInvoker is null && !isPredicate)
        {
            throw new ArgumentException("A non-predicate function requires an invoker.", nameof(invoker));
        }

        Name = name;
        Overloads = Array.AsReadOnly(overloadSnapshot);
        Invoker = invoker;
        SafeInvoker = safeInvoker;
        TypeValidator = typeValidator;
        IsPredicate = isPredicate;
        MemoryEstimator = memoryEstimator;
        EnforceRuntimeArity = enforceRuntimeArity;
    }

    /// <summary>Gets the expression-visible name.</summary>
    public string Name { get; }

    /// <summary>Gets the statically checkable overloads.</summary>
    public IReadOnlyList<ExprFunctionOverload> Overloads { get; }

    /// <summary>Gets the ordinary invoker, when present.</summary>
    public ExprFunctionInvoker? Invoker { get; }

    /// <summary>Gets the resource-accounting invoker, when present.</summary>
    public ExprSafeFunctionInvoker? SafeInvoker { get; }

    /// <summary>Gets the custom type validator, when present.</summary>
    public ExprFunctionTypeValidator? TypeValidator { get; }

    /// <summary>Gets a value indicating whether the function consumes a compiler-provided predicate.</summary>
    public bool IsPredicate { get; }

    /// <summary>Gets the optional estimator used to reject over-budget calls before host allocation.</summary>
    public ExprFunctionMemoryEstimator? MemoryEstimator { get; }

    internal bool EnforceRuntimeArity { get; }

    /// <summary>Estimates an allocation upper bound for the supplied arguments.</summary>
    /// <param name="arguments">The ordered runtime arguments.</param>
    /// <returns>The estimated allocation charge, or zero when no estimator is registered.</returns>
    public ulong EstimateMemoryCost(ReadOnlySpan<object?> arguments) => MemoryEstimator?.Invoke(arguments) ?? 0;

    /// <summary>Invokes the function and returns its value and resource charge.</summary>
    /// <param name="arguments">The ordered runtime arguments.</param>
    /// <returns>The invocation result.</returns>
    /// <exception cref="ExprRuntimeException">No overload accepts the arity or no runtime invoker exists.</exception>
    public ExprInvocationResult Invoke(ReadOnlySpan<object?> arguments)
    {
        bool acceptsArity = IsPredicate || Overloads.Count is 0;
        for (int index = 0; index < Overloads.Count && !acceptsArity; index++)
        {
            acceptsArity = Overloads[index].AcceptsArity(arguments.Length);
        }

        if (EnforceRuntimeArity && !acceptsArity)
        {
            throw new ExprRuntimeException(
                $"invalid number of arguments for {Name} (got {arguments.Length})");
        }

        if (SafeInvoker is not null)
        {
            return SafeInvoker(arguments);
        }

        if (Invoker is not null)
        {
            return new ExprInvocationResult(Invoker(arguments), 0);
        }

        throw new ExprRuntimeException($"function {Name} cannot be invoked directly");
    }
}
