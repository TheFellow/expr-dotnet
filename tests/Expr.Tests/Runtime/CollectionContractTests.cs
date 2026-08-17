using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Expr.Runtime;
using Xunit;

namespace Expr.Tests.Runtime;

public sealed class CollectionContractTests
{
    private static readonly byte[] ByteValues = [1, 2];
    private static readonly int[] IntegerValues = [1, 2];

    [Fact]
    public void Public_array_and_map_values_expose_consistent_generic_and_nongeneric_views()
    {
        var array = new ExprArray([1L, "two"]);
        var map = new ExprMap(
        [
            new KeyValuePair<object?, object?>(null, "nil"),
            new KeyValuePair<object?, object?>("answer", 42L),
        ]);

        Assert.Equal([1L, "two"], ((IEnumerable)array).Cast<object?>());
        Assert.Equal(typeof(object), map.KeyType);
        Assert.Equal(typeof(object), map.ValueType);
        Assert.Equal(2, map.Count);
        Assert.True(map.TryGetValue(null, out object? nil));
        Assert.Equal("nil", nil);
        Assert.Equal(2, ((IEnumerable)map).Cast<object>().Count());
    }

    [Fact]
    public void Public_map_rejects_duplicate_null_keys_at_construction()
    {
        KeyValuePair<object?, object?>[] values =
        [
            new(null, 1L),
            new(null, 2L),
        ];

        Assert.Throws<ArgumentException>(() => new ExprMap(values));
    }

    [Fact]
    public void Reflection_free_collection_adapters_are_live_typed_views()
    {
        var list = new List<long> { 1, 2 };
        var dictionary = new Dictionary<uint, string> { [1U] = "one" };
        var array = new ExprReadOnlyListAdapter<long>(list);
        var map = new ExprReadOnlyDictionaryAdapter<uint, string>(dictionary);

        list.Add(3);
        dictionary[2U] = "two";

        Assert.Equal(typeof(long), array.ElementType);
        Assert.Equal([1L, 2L, 3L], ((IEnumerable)array).Cast<object?>());
        Assert.Equal(typeof(uint), map.KeyType);
        Assert.Equal(typeof(string), map.ValueType);
        Assert.Equal(2, map.Count);
        Assert.True(map.TryGetValue(2L, out object? value));
        Assert.Equal("two", value);
        Assert.Equal(2, ((IEnumerable)map).Cast<object>().Count());
    }

    [Fact]
    public void Integer_map_keys_convert_to_every_supported_clr_integral_type()
    {
        AssertIntegerKey(new Dictionary<sbyte, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<byte, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<short, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<ushort, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<int, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<uint, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<long, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<ulong, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<nint, string> { [1] = "value" });
        AssertIntegerKey(new Dictionary<nuint, string> { [1] = "value" });

        var bytes = new ExprReadOnlyDictionaryAdapter<byte, string>(
            new Dictionary<byte, string> { [1] = "value" });
        Assert.False(bytes.TryGetValue(256L, out _));
    }

    [Fact]
    public void Public_collection_detection_adapts_standard_clr_shapes()
    {
        Assert.True(ExprCollections.TryAsArray(new ReadOnlyMemory<byte>(ByteValues), out IExprArray? bytes));
        Assert.Equal([1, 2], bytes!.Cast<byte>());

        Assert.True(ExprCollections.TryAsArray(IntegerValues, out IExprArray? array));
        Assert.Equal([1, 2], array!.Cast<int>());

        Assert.True(ExprCollections.TryAsArray(new ArrayList { 1, 2 }, out IExprArray? list));
        Assert.Equal([1, 2], list!.Cast<int>());

        var table = new Hashtable { ["answer"] = 42 };
        Assert.True(ExprCollections.TryAsMap(table, out IExprMap? map));
        Assert.Equal(typeof(object), map!.KeyType);
        Assert.Equal(typeof(object), map.ValueType);
        Assert.True(map.TryGetValue("answer", out object? answer));
        Assert.Equal(42, answer);
        Assert.Single(((IEnumerable)map).Cast<object>());
    }

    private static void AssertIntegerKey<TKey>(IReadOnlyDictionary<TKey, string> values)
        where TKey : notnull
    {
        var adapter = new ExprReadOnlyDictionaryAdapter<TKey, string>(values);

        Assert.True(adapter.TryGetValue(1L, out object? value));
        Assert.Equal("value", value);
    }
}
