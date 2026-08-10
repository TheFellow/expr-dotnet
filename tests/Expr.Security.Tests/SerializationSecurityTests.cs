using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Expr.Builtins;
using Expr.Runtime;
using Xunit;

namespace Expr.Security.Tests;

public sealed class SerializationSecurityTests
{
    [Fact]
    public void Json_rejects_hostile_array_count_before_indexing_or_enumerating()
    {
        var hostile = new HostileArray(33);
        var library = new ExprBuiltinLibrary(new ExprBuiltinOptions { MaximumAllocation = 32 });

        ExprRuntimeException exception = Assert.Throws<ExprRuntimeException>(() =>
            library.Get("toJSON").Invoke([hostile]));

        Assert.Contains("memory budget exceeded", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, hostile.IndexAttempts);
        Assert.Equal(0, hostile.EnumerationAttempts);
    }

    [Fact]
    public void Json_rejects_hostile_map_count_before_enumerating()
    {
        var hostile = new HostileMap(33);
        var library = new ExprBuiltinLibrary(new ExprBuiltinOptions { MaximumAllocation = 32 });

        ExprRuntimeException exception = Assert.Throws<ExprRuntimeException>(() =>
            library.Get("toJSON").Invoke([hostile]));

        Assert.Contains("memory budget exceeded", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, hostile.EnumerationAttempts);
    }

    [Fact]
    public void Json_depth_and_allocation_limits_apply_before_materialized_results_escape()
    {
        var depthLibrary = new ExprBuiltinLibrary(new ExprBuiltinOptions
        {
            MaximumDepth = 4,
            MaximumAllocation = 1_024,
        });
        var allocationLibrary = new ExprBuiltinLibrary(new ExprBuiltinOptions
        {
            MaximumDepth = 32,
            MaximumAllocation = 32,
        });

        Assert.Throws<ExprRuntimeException>(() =>
            depthLibrary.Get("fromJSON").Invoke(["[[[[[0]]]]]"]));
        Assert.Throws<ExprRuntimeException>(() =>
            allocationLibrary.Get("fromJSON").Invoke(["[\"abcdefghijklmnopqrstuvwxyz\", 1, 2, 3]"]));
        Assert.Throws<ExprRuntimeException>(() =>
            allocationLibrary.Get("toJSON").Invoke([new string('<', 16)]));
    }

    [Fact]
    public void Json_cycle_detection_terminates_without_process_recursion()
    {
        var cycle = new ExprArrayBuilder();
        cycle.Add(cycle);
        var library = new ExprBuiltinLibrary(new ExprBuiltinOptions
        {
            MaximumDepth = 32,
            MaximumAllocation = 1_024,
        });

        ExprRuntimeException exception = Assert.Throws<ExprRuntimeException>(() =>
            library.Get("toJSON").Invoke([cycle]));

        Assert.Contains("cycle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_rejects_poco_values_without_invoking_host_getters()
    {
        var value = new IgnoredGetterHost();
        var library = new ExprBuiltinLibrary();

        ExprRuntimeException exception = Assert.Throws<ExprRuntimeException>(() =>
            library.Get("toJSON").Invoke([value]));

        Assert.Contains("unsupported value", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, value.AllowedGetterAttempts);
        Assert.Equal(0, value.SecretGetterAttempts);
    }

    private sealed class HostileArray(int count) : IExprArray
    {
        public Type ElementType => typeof(object);

        public int Count { get; } = count;

        public int IndexAttempts { get; private set; }

        public int EnumerationAttempts { get; private set; }

        public object? this[int index]
        {
            get
            {
                IndexAttempts++;
                throw new InvalidOperationException("Index access was not authorized.");
            }
        }

        public IEnumerator<object?> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidOperationException("Enumeration was not authorized.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class HostileMap(int count) : IExprMap
    {
        public Type KeyType => typeof(string);

        public Type ValueType => typeof(object);

        public int Count { get; } = count;

        public int EnumerationAttempts { get; private set; }

        public object? this[object? key] => throw new InvalidOperationException("Indexer access was not authorized.");

        public bool ContainsKey(object? key) => throw new InvalidOperationException("Lookup was not authorized.");

        public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidOperationException("Enumeration was not authorized.");
        }

        public bool TryGetValue(object? key, [MaybeNullWhen(false)] out object? value)
        {
            throw new InvalidOperationException("Lookup was not authorized.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ExprArrayBuilder : IExprArray
    {
        private readonly List<object?> items = [];

        public Type ElementType => typeof(object);

        public int Count => items.Count;

        public object? this[int index] => items[index];

        public void Add(object? value) => items.Add(value);

        public IEnumerator<object?> GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class IgnoredGetterHost
    {
        [JsonIgnore]
        public int AllowedGetterAttempts { get; private set; }

        public string Allowed
        {
            get
            {
                AllowedGetterAttempts++;
                return "safe";
            }
        }

        [JsonIgnore]
        public int SecretGetterAttempts { get; private set; }

        [JsonIgnore]
        public string Secret
        {
            get
            {
                SecretGetterAttempts++;
                throw new InvalidOperationException("Ignored getter was invoked.");
            }
        }
    }
}
