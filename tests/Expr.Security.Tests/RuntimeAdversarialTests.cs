using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Expr.Runtime;
using Xunit;

namespace Expr.Security.Tests;

public sealed class RuntimeAdversarialTests
{
    [Fact]
    public void Equality_terminates_for_mutually_cyclic_arrays()
    {
        var left = new CyclicArray();
        var right = new CyclicArray();

        Assert.True(ExprValue.Equal(left, right));
    }

    [Fact]
    public void Equality_rejects_excessive_depth_without_using_the_process_stack()
    {
        object left = 1L;
        object right = 1L;
        for (var depth = 0; depth <= ExprValue.MaximumEqualityDepth; depth++)
        {
            left = new ExprArray([left]);
            right = new ExprArray([right]);
        }

        ExprRuntimeException exception = Assert.Throws<ExprRuntimeException>(() => ExprValue.Equal(left, right));

        Assert.Equal("recursion depth exceeded", exception.Message);
    }

    [Fact]
    public void Array_metadata_access_does_not_enumerate_a_hostile_collection()
    {
        var hostile = new HostileReadOnlyList();
        Assert.False(ExprCollections.TryAsArray(hostile, out _));
        ExprReadOnlyListAdapter<object?> adapter = ExprCollections.AsArray(hostile);

        int length = ExprValue.StorageLength(adapter);

        Assert.Equal(7, length);
        Assert.Equal(0, hostile.EnumerationAttempts);
        Assert.Equal("requested", ExprValue.FetchIndex(adapter, 3));
        Assert.Equal(3, hostile.LastRequestedIndex);
    }

    [Fact]
    public void Dictionary_membership_uses_typed_lookup_without_enumerating()
    {
        var hostile = new HostileReadOnlyDictionary();
        Assert.False(ExprCollections.TryAsMap(hostile, out _));
        ExprReadOnlyDictionaryAdapter<string, int> adapter = ExprCollections.AsMap(hostile);

        Assert.True(ExprValue.In("allowed", adapter));
        Assert.False(ExprValue.In(42, adapter));

        Assert.Equal(1, hostile.LookupAttempts);
        Assert.Equal(0, hostile.EnumerationAttempts);
    }

    [Fact]
    public void Runtime_errors_do_not_call_or_disclose_host_object_stringification()
    {
        var secret = new HostileDisplayObject();

        ExprRuntimeException exception = Assert.Throws<ExprRuntimeException>(() => ExprValue.ToInt64(secret));

        Assert.Equal(0, secret.StringificationAttempts);
        Assert.DoesNotContain(HostileDisplayObject.Secret, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(HostileDisplayObject).FullName!, exception.Message, StringComparison.Ordinal);
    }

    private sealed class CyclicArray : IExprArray
    {
        public Type ElementType => typeof(object);

        public int Count => 1;

        public object this[int index] => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<object?> GetEnumerator()
        {
            yield return this;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class HostileReadOnlyList : IReadOnlyList<object?>
    {
        public int EnumerationAttempts { get; private set; }

        public int LastRequestedIndex { get; private set; } = -1;

        public int Count => 7;

        public object this[int index]
        {
            get
            {
                LastRequestedIndex = index;
                return "requested";
            }
        }

        public IEnumerator<object?> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidOperationException("Enumeration was not authorized by this test.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class HostileReadOnlyDictionary : IReadOnlyDictionary<string, int>
    {
        public int LookupAttempts { get; private set; }

        public int EnumerationAttempts { get; private set; }

        public IEnumerable<string> Keys => throw new InvalidOperationException("Key enumeration was not authorized.");

        public IEnumerable<int> Values => throw new InvalidOperationException("Value enumeration was not authorized.");

        public int Count => 1;

        public int this[string key] => throw new InvalidOperationException($"Indexer access was not authorized for {key}.");

        public bool ContainsKey(string key) => throw new InvalidOperationException($"ContainsKey was not authorized for {key}.");

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidOperationException("Enumeration was not authorized.");
        }

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out int value)
        {
            LookupAttempts++;
            value = 1;
            return string.Equals(key, "allowed", StringComparison.Ordinal);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class HostileDisplayObject
    {
        public const string Secret = "DO-NOT-DISCLOSE-THIS-VALUE";

        public int StringificationAttempts { get; private set; }

        public override string ToString()
        {
            StringificationAttempts++;
            return Secret;
        }
    }
}
