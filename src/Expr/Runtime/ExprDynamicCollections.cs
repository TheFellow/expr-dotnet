using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace Expr.Runtime;

/// <summary>
/// Discovers generic collection contracts from runtime types for hosts that support reflection and dynamic code.
/// </summary>
/// <remarks>
/// Trimmed and Native AOT applications should use <see cref="ExprCollections.AsArray{T}(IReadOnlyList{T})"/>
/// and <see cref="ExprCollections.AsMap{TKey,TValue}(IReadOnlyDictionary{TKey,TValue})"/> instead.
/// </remarks>
public static class ExprDynamicCollections
{
    private static readonly ConcurrentDictionary<Type, Func<object, IExprArray?>> ReadOnlyListFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, IExprMap?>> ReadOnlyDictionaryFactories = new();
    private static readonly MethodInfo CreateReadOnlyListFactoryMethod = typeof(ExprDynamicCollections)
        .GetMethod(nameof(CreateReadOnlyListFactory), BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException("The read-only list adapter factory is unavailable.");
    private static readonly MethodInfo CreateReadOnlyDictionaryFactoryMethod = typeof(ExprDynamicCollections)
        .GetMethod(nameof(CreateReadOnlyDictionaryFactory), BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException("The read-only dictionary adapter factory is unavailable.");

    /// <summary>Attempts to discover and adapt an array-like runtime value.</summary>
    /// <param name="value">The host value.</param>
    /// <param name="array">The adapted array.</param>
    /// <returns><see langword="true"/> when the value is array-like.</returns>
    [RequiresDynamicCode("Generic collection adapters are closed over element types discovered at runtime. Use ExprCollections.AsArray<T> for Native AOT.")]
    [RequiresUnreferencedCode("Generic collection interfaces must be preserved for runtime discovery. Use ExprCollections.AsArray<T> for trimming.")]
    public static bool TryAsArray(object? value, out IExprArray? array)
    {
        if (value is null)
        {
            array = null;
            return false;
        }

        Func<object, IExprArray?> factory = ReadOnlyListFactories.GetOrAdd(
            value.GetType(),
            static type => BuildReadOnlyListFactory(type));
        array = factory(value);
        return array is not null || ExprCollections.TryAsArray(value, out array);
    }

    /// <summary>Attempts to discover and adapt a map-like runtime value.</summary>
    /// <param name="value">The host value.</param>
    /// <param name="map">The adapted map.</param>
    /// <returns><see langword="true"/> when the value is map-like.</returns>
    [RequiresDynamicCode("Generic collection adapters are closed over key and value types discovered at runtime. Use ExprCollections.AsMap<TKey, TValue> for Native AOT.")]
    [RequiresUnreferencedCode("Generic collection interfaces must be preserved for runtime discovery. Use ExprCollections.AsMap<TKey, TValue> for trimming.")]
    public static bool TryAsMap(object? value, out IExprMap? map)
    {
        if (value is null)
        {
            map = null;
            return false;
        }

        Func<object, IExprMap?> factory = ReadOnlyDictionaryFactories.GetOrAdd(
            value.GetType(),
            static type => BuildReadOnlyDictionaryFactory(type));
        map = factory(value);
        return map is not null || ExprCollections.TryAsMap(value, out map);
    }

    [RequiresDynamicCode("Closes a generic adapter over a runtime element type.")]
    [RequiresUnreferencedCode("Inspects generic collection contracts on a runtime type.")]
    private static Func<object, IExprArray?> BuildReadOnlyListFactory(Type type)
    {
        Type? contract = type.GetInterfaces().Append(type)
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
        if (contract is null)
        {
            return static _ => null;
        }

        Type elementType = contract.GetGenericArguments()[0];
        MethodInfo factory = CreateReadOnlyListFactoryMethod.MakeGenericMethod(elementType);
        return factory.Invoke(null, null) as Func<object, IExprArray> ??
            throw new InvalidOperationException("The read-only list adapter could not be created.");
    }

    private static Func<object, IExprArray> CreateReadOnlyListFactory<T>() =>
        static value => new ExprReadOnlyListAdapter<T>((IReadOnlyList<T>)value);

    [RequiresDynamicCode("Closes a generic adapter over runtime key and value types.")]
    [RequiresUnreferencedCode("Inspects generic collection contracts on a runtime type.")]
    private static Func<object, IExprMap?> BuildReadOnlyDictionaryFactory(Type type)
    {
        Type? contract = type.GetInterfaces().Append(type)
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
        if (contract is null)
        {
            return static _ => null;
        }

        Type[] arguments = contract.GetGenericArguments();
        MethodInfo factory = CreateReadOnlyDictionaryFactoryMethod.MakeGenericMethod(arguments);
        return factory.Invoke(null, null) as Func<object, IExprMap> ??
            throw new InvalidOperationException("The read-only dictionary adapter could not be created.");
    }

    private static Func<object, IExprMap> CreateReadOnlyDictionaryFactory<TKey, TValue>()
        where TKey : notnull =>
        static value => new ExprReadOnlyDictionaryAdapter<TKey, TValue>((IReadOnlyDictionary<TKey, TValue>)value);
}
