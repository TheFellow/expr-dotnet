using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace Expr.Runtime;

/// <summary>Provides indexed access to an Expr array value.</summary>
public interface IExprArray : IEnumerable<object?>
{
    /// <summary>
    /// Gets the declared CLR element type, or <see cref="object"/> for a dynamically typed array.
    /// </summary>
    Type ElementType { get; }

    /// <summary>Gets the number of elements.</summary>
    int Count { get; }

    /// <summary>Gets an element by zero-based index.</summary>
    /// <param name="index">The element index.</param>
    /// <returns>The element.</returns>
    object? this[int index] { get; }
}

/// <summary>Provides key-based access to an Expr map value.</summary>
public interface IExprMap : IEnumerable<KeyValuePair<object?, object?>>
{
    /// <summary>Gets the declared CLR key type.</summary>
    Type KeyType { get; }

    /// <summary>Gets the declared CLR value type.</summary>
    Type ValueType { get; }

    /// <summary>Gets the number of entries.</summary>
    int Count { get; }

    /// <summary>Attempts to get the value associated with a key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The associated value.</param>
    /// <returns><see langword="true"/> when the key exists.</returns>
    bool TryGetValue(object? key, out object? value);
}

/// <summary>An immutable Expr array backed by a snapshot of an idiomatic .NET sequence.</summary>
public sealed class ExprArray : IExprArray, IReadOnlyList<object?>
{
    private readonly IReadOnlyList<object?> values;

    /// <summary>
    /// Initializes an array from a sequence and takes an immutable snapshot.
    /// </summary>
    /// <param name="values">The values to copy.</param>
    public ExprArray(IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = Array.AsReadOnly(values.ToArray());
    }

    /// <inheritdoc />
    public Type ElementType => typeof(object);

    /// <inheritdoc />
    public int Count => values.Count;

    /// <inheritdoc />
    public object? this[int index] => values[index];

    /// <inheritdoc />
    public IEnumerator<object?> GetEnumerator() => values.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>An immutable Expr map backed by a snapshot of key-value pairs.</summary>
public sealed class ExprMap : IExprMap
{
    private readonly IReadOnlyDictionary<object, object?> values;
    private readonly bool hasNullKey;
    private readonly object? nullValue;

    /// <summary>
    /// Initializes a map from key-value pairs and takes an immutable snapshot.
    /// </summary>
    /// <param name="values">The entries to copy.</param>
    public ExprMap(IEnumerable<KeyValuePair<object?, object?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<object, object?>();
        foreach ((object? key, object? value) in values)
        {
            if (key is null)
            {
                if (hasNullKey)
                {
                    throw new ArgumentException("The input contains more than one null key.", nameof(values));
                }

                hasNullKey = true;
                nullValue = value;
            }
            else
            {
                copy.Add(key, value);
            }
        }

        this.values = new ReadOnlyDictionary<object, object?>(copy);
    }

    /// <inheritdoc />
    public Type KeyType => typeof(object);

    /// <inheritdoc />
    public Type ValueType => typeof(object);

    /// <inheritdoc />
    public int Count => values.Count + (hasNullKey ? 1 : 0);

    /// <inheritdoc />
    public bool TryGetValue(object? key, out object? value)
    {
        if (key is null)
        {
            value = nullValue;
            return hasNullKey;
        }

        return values.TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator()
    {
        if (hasNullKey)
        {
            yield return new KeyValuePair<object?, object?>(null, nullValue);
        }

        foreach ((object key, object? value) in values)
        {
            yield return new KeyValuePair<object?, object?>(key, value);
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Adapts common .NET collection contracts to Expr collection access.</summary>
public static class ExprCollections
{
    private static readonly ConcurrentDictionary<Type, Func<object, IExprArray?>> ReadOnlyListFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, IExprMap?>> ReadOnlyDictionaryFactories = new();
    private static readonly MethodInfo CreateReadOnlyListFactoryMethod = typeof(ExprCollections)
        .GetMethod(nameof(CreateReadOnlyListFactory), BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException("The read-only list adapter factory is unavailable.");
    private static readonly MethodInfo CreateReadOnlyDictionaryFactoryMethod = typeof(ExprCollections)
        .GetMethod(nameof(CreateReadOnlyDictionaryFactory), BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException("The read-only dictionary adapter factory is unavailable.");

    /// <summary>Attempts to adapt a value to an Expr array.</summary>
    /// <param name="value">The host value.</param>
    /// <param name="array">The adapted array.</param>
    /// <returns><see langword="true"/> when the value is array-like.</returns>
    public static bool TryAsArray(object? value, out IExprArray? array)
    {
        if (value is null)
        {
            array = null;
            return false;
        }

        switch (value)
        {
            case IExprArray exprArray:
                array = exprArray;
                return true;
            case Array clrArray when clrArray.Rank is 1:
                array = new ClrArrayAdapter(clrArray);
                return true;
            case IList list:
                array = new ListAdapter(list);
                return true;
            default:
                Func<object, IExprArray?> factory = ReadOnlyListFactories.GetOrAdd(
                    value.GetType(),
                    static type => BuildReadOnlyListFactory(type));
                array = factory(value);
                return array is not null;
        }
    }

    /// <summary>Attempts to adapt a value to an Expr map.</summary>
    /// <param name="value">The host value.</param>
    /// <param name="map">The adapted map.</param>
    /// <returns><see langword="true"/> when the value is map-like.</returns>
    public static bool TryAsMap(object? value, out IExprMap? map)
    {
        if (value is null)
        {
            map = null;
            return false;
        }

        switch (value)
        {
            case IExprMap exprMap:
                map = exprMap;
                return true;
            case IDictionary dictionary:
                map = new DictionaryAdapter(dictionary);
                return true;
            default:
                Func<object, IExprMap?> factory = ReadOnlyDictionaryFactories.GetOrAdd(
                    value.GetType(),
                    static type => BuildReadOnlyDictionaryFactory(type));
                map = factory(value);
                return map is not null;
        }
    }

    private sealed class ClrArrayAdapter(Array array) : IExprArray
    {
        public Type ElementType => array.GetType().GetElementType() ?? typeof(object);

        public int Count => array.Length;

        public object? this[int index] => array.GetValue(index);

        public IEnumerator<object?> GetEnumerator()
        {
            foreach (object? value in array)
            {
                yield return value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ListAdapter(IList list) : IExprArray
    {
        public Type ElementType { get; } = GetListElementType(list.GetType()) ?? typeof(object);

        public int Count => list.Count;

        public object? this[int index] => list[index];

        public IEnumerator<object?> GetEnumerator()
        {
            foreach (object? value in list)
            {
                yield return value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DictionaryAdapter(IDictionary dictionary) : IExprMap
    {
        public Type KeyType { get; } = GetDictionaryTypes(dictionary.GetType()).Key;

        public Type ValueType { get; } = GetDictionaryTypes(dictionary.GetType()).Value;

        public int Count => dictionary.Count;

        public bool TryGetValue(object? key, out object? value)
        {
            if (key is null)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is null)
                    {
                        value = entry.Value;
                        return true;
                    }
                }
            }
            else if (dictionary.Contains(key))
            {
                value = dictionary[key];
                return true;
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator()
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                yield return new KeyValuePair<object?, object?>(entry.Key, entry.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ReadOnlyListAdapter<T>(IReadOnlyList<T> list) : IExprArray
    {
        public Type ElementType => typeof(T);

        public int Count => list.Count;

        public object? this[int index] => list[index];

        public IEnumerator<object?> GetEnumerator()
        {
            foreach (T value in list)
            {
                yield return value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ReadOnlyDictionaryAdapter<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary) : IExprMap
        where TKey : notnull
    {
        public Type KeyType => typeof(TKey);

        public Type ValueType => typeof(TValue);

        public int Count => dictionary.Count;

        public bool TryGetValue(object? key, out object? value)
        {
            if (key is TKey typedKey && dictionary.TryGetValue(typedKey, out TValue? typedValue))
            {
                value = typedValue;
                return true;
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator()
        {
            foreach ((TKey key, TValue value) in dictionary)
            {
                yield return new KeyValuePair<object?, object?>(key, value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static Type? GetListElementType(Type type) => type.GetInterfaces().Append(type)
        .FirstOrDefault(static candidate =>
            candidate.IsGenericType &&
            (candidate.GetGenericTypeDefinition() == typeof(IList<>) ||
             candidate.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
        ?.GetGenericArguments()[0];

    private static (Type Key, Type Value) GetDictionaryTypes(Type type)
    {
        Type? contract = type.GetInterfaces().Append(type)
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType &&
                (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        Type[]? arguments = contract?.GetGenericArguments();
        return arguments is null ? (typeof(object), typeof(object)) : (arguments[0], arguments[1]);
    }

    private static Func<object, IExprArray?> BuildReadOnlyListFactory(Type type)
    {
        Type? elementType = GetListElementType(type);
        if (elementType is null)
        {
            return static _ => null;
        }

        MethodInfo factory = CreateReadOnlyListFactoryMethod.MakeGenericMethod(elementType);
        var adapter = factory.Invoke(null, null) as Func<object, IExprArray> ??
            throw new InvalidOperationException("The read-only list adapter could not be created.");
        return value => adapter(value);
    }

    private static Func<object, IExprArray> CreateReadOnlyListFactory<T>() =>
        static value => new ReadOnlyListAdapter<T>((IReadOnlyList<T>)value);

    private static Func<object, IExprMap?> BuildReadOnlyDictionaryFactory(Type type)
    {
        (Type key, Type value) = GetDictionaryTypes(type);
        if (key == typeof(object) && value == typeof(object))
        {
            return static _ => null;
        }

        MethodInfo factory = CreateReadOnlyDictionaryFactoryMethod.MakeGenericMethod(key, value);
        var adapter = factory.Invoke(null, null) as Func<object, IExprMap> ??
            throw new InvalidOperationException("The read-only dictionary adapter could not be created.");
        return value => adapter(value);
    }

    private static Func<object, IExprMap> CreateReadOnlyDictionaryFactory<TKey, TValue>()
        where TKey : notnull =>
        static value => new ReadOnlyDictionaryAdapter<TKey, TValue>((IReadOnlyDictionary<TKey, TValue>)value);
}
