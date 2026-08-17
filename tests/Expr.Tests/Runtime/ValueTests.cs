using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Expr.Runtime;
using Xunit;

namespace Expr.Tests.Runtime;

public sealed class ValueTests
{
    [Fact]
    public void Read_only_byte_memory_has_expr_array_and_value_equality_semantics()
    {
        ReadOnlyMemory<byte> left = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte> right = new byte[] { 1, 2, 3 };

        Assert.Equal(ExprValueKind.Array, ExprValue.Classify(left));
        Assert.True(ExprValue.Equal(left, right));
        Assert.True(ExprValue.In(2L, left));
        Assert.Equal(3, ExprValue.StorageLength(left));
    }

    [Fact]
    public void Equal_does_not_make_a_shared_boxed_nan_equal_to_itself()
    {
        object nan = double.NaN;

        Assert.False(ExprValue.Equal(nan, nan));
    }

    private static readonly object?[] NestedArray = [1, new[] { 2, 3 }];
    private static readonly List<object?> NestedList = [1L, new object[] { 2L, 3L }];
    private static readonly int[] TypedIntegers = [1];
    private static readonly long[] TypedLongs = [1];
    private static readonly object[] DynamicIntegers = [1];

    public static TheoryData<object, object, bool> EqualityCases => new()
    {
        { 42, 42L, true },
        { 42, (byte)42, true },
        { 42F, 42D, true },
        { 42D, 42, true },
        { 42D, 33, false },
        { "foo", "foo", true },
        { true, false, false },
        { NestedArray, NestedList, true },
        { new Dictionary<string, object?> { ["a"] = 1 }, new Dictionary<string, object?> { ["a"] = 1L }, true },
    };

    [Theory]
    [MemberData(nameof(EqualityCases))]
    public void Equal_matches_expr_cross_host_numeric_and_deep_collection_semantics(
        object left,
        object right,
        bool expected)
    {
        Assert.Equal(expected, ExprValue.Equal(left, right));
        Assert.Equal(expected, ExprValue.Equal(right, left));
    }

    [Fact]
    public void Equal_handles_reference_cycles_without_unbounded_recursion()
    {
        var left = new List<object?>();
        var right = new List<object?>();
        left.Add(left);
        right.Add(right);

        Assert.True(ExprValue.Equal(left, right));
    }

    [Fact]
    public void Equal_preserves_typed_array_identity_unless_one_side_is_dynamic()
    {
        Assert.False(ExprValue.Equal(TypedIntegers, TypedLongs));
        Assert.True(ExprValue.Equal(DynamicIntegers, TypedLongs));
    }

    [Fact]
    public void Equal_enforces_depth_limit_without_using_the_clr_call_stack()
    {
        object left = 1;
        object right = 1;
        for (int depth = 0; depth <= ExprValue.MaximumEqualityDepth; depth++)
        {
            left = new ExprArray([left]);
            right = new ExprArray([right]);
        }

        Assert.Throws<ExprRuntimeException>(() => ExprValue.Equal(left, right));
    }

    [Fact]
    public void Time_equality_and_ordering_compare_instants()
    {
        DateTimeOffset utc = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset local = utc.ToOffset(TimeSpan.FromHours(-7));

        Assert.True(ExprValue.Equal(utc, local));
        Assert.Equal(0, ExprValue.Compare(utc, local));
        Assert.True(ExprValue.Less(utc, utc.AddTicks(1)));
    }

    [Fact]
    public void Compare_supports_numbers_strings_and_durations()
    {
        Assert.True(ExprValue.Less(1, 1.5D));
        Assert.True(ExprValue.Less("a", "b"));
        Assert.True(ExprValue.Less(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        Assert.Throws<ExprRuntimeException>(() => ExprValue.Compare(true, false));
        Assert.False(ExprValue.Equal(double.NaN, double.NaN));
        Assert.False(ExprValue.Less(double.NaN, 1D));
        Assert.Throws<ExprRuntimeException>(() => ExprValue.Compare(double.NaN, 1D));
    }

    [Fact]
    public void Conversion_is_strict_and_culture_independent()
    {
        Assert.Equal(42L, ExprValue.ToInt64(42D));
        Assert.Equal(42D, ExprValue.ToDouble(42L));
        Assert.False(ExprValue.ToBoolean(null));
        Assert.Throws<ExprRuntimeException>(() => ExprValue.ToInt64("42"));
        Assert.Throws<ExprRuntimeException>(() => ExprValue.ToBoolean(1));
    }

    [Fact]
    public void Public_value_contract_covers_all_clr_scalar_families()
    {
        Assert.Equal(ExprValueKind.Nil, ExprValue.Classify(null));
        Assert.Equal(ExprValueKind.Boolean, ExprValue.Classify(true));
        Assert.Equal(ExprValueKind.SignedInteger, ExprValue.Classify((nint)1));
        Assert.Equal(ExprValueKind.UnsignedInteger, ExprValue.Classify((nuint)1));
        Assert.Equal(ExprValueKind.Float, ExprValue.Classify((Half)1));
        Assert.Equal(ExprValueKind.String, ExprValue.Classify("value"));
        Assert.Equal(ExprValueKind.Time, ExprValue.Classify(DateTime.UtcNow));
        Assert.Equal(ExprValueKind.Duration, ExprValue.Classify(TimeSpan.Zero));
        Assert.Equal(ExprValueKind.Function, ExprValue.Classify(new Func<int>(static () => 1)));
        Assert.Equal(ExprValueKind.Map, ExprValue.Classify(new Dictionary<string, int>()));
        Assert.Equal(ExprValueKind.Object, ExprValue.Classify(new object()));

        object[] numbers =
        [
            (sbyte)1, (byte)1, (short)1, (ushort)1, 1, 1U, 1L, 1UL,
            (nint)1, (nuint)1, (Half)1, 1F, 1D,
        ];
        foreach (object number in numbers)
        {
            Assert.Equal(1L, ExprValue.ToInt64(number));
            Assert.Equal(1D, ExprValue.ToDouble(number));
        }
    }

    [Fact]
    public void Collection_helpers_support_negative_indices_membership_and_length()
    {
        int[] values = [10, 20, 30];
        var map = new Dictionary<string, int> { ["answer"] = 42 };

        Assert.Equal(30, ExprValue.FetchIndex(values, -1));
        Assert.Equal((byte)'b', ExprValue.FetchIndex("abc", 1));
        Assert.Equal(3, ExprValue.StorageLength(values));
        Assert.True(ExprValue.In(20L, values));
        Assert.True(ExprValue.In("answer", map));
        Assert.False(ExprValue.In("missing", map));
        Assert.Throws<ExprRuntimeException>(() => ExprValue.FetchIndex(values, 3));
        Assert.Equal(4, ExprValue.StorageLength("🐧"));
        Assert.Equal((byte)0xF0, ExprValue.FetchIndex("🐧", 0));
        Assert.Equal(1, ExprValue.StorageLength(map));
        Assert.Throws<ExprRuntimeException>(() => ExprValue.FetchIndex(map, 0));
        Assert.Throws<ExprRuntimeException>(() => ExprValue.In(1, 42));
    }

    [Fact]
    public void Public_equality_covers_temporal_and_map_mismatch_paths()
    {
        Assert.True(ExprValue.Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.True(ExprValue.Equal(
            DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc),
            DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Unspecified)));
        Assert.True(ExprValue.Equal(
            DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Local),
            new DateTimeOffset(DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Local))));
        Assert.False(ExprValue.Equal(
            new Dictionary<string, int> { ["a"] = 1 },
            new Dictionary<string, int>()));
        Assert.False(ExprValue.Equal(
            new Dictionary<string, int> { ["a"] = 1 },
            new Dictionary<string, int> { ["b"] = 1 }));
    }

    [Fact]
    public void Immutable_collection_wrappers_take_snapshots()
    {
        var source = new List<object?> { 1, 2 };
        var array = new ExprArray(source);
        source.Add(3);

        var map = new ExprMap(
            [
                new KeyValuePair<object?, object?>("a", 1),
                new KeyValuePair<object?, object?>(null, 2),
            ]);

        Assert.Equal(2, array.Count);
        Assert.True(map.TryGetValue(null, out object? value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void Collection_adapters_support_pure_generic_read_only_contracts()
    {
        var sequence = new ReadOnlySequence<int>([1, 2, 3]);
        var map = new ReadOnlyMap<string, int>(new Dictionary<string, int> { ["a"] = 1 });
        ExprReadOnlyListAdapter<int> arrayAdapter = ExprCollections.AsArray(sequence);
        ExprReadOnlyDictionaryAdapter<string, int> mapAdapter = ExprCollections.AsMap(map);

        Assert.Equal(3, ExprValue.StorageLength(arrayAdapter));
        Assert.Equal(2, ExprValue.FetchIndex(arrayAdapter, 1));
        Assert.True(ExprValue.In("a", mapAdapter));
        Assert.True(ExprValue.Equal(arrayAdapter, ExprCollections.AsArray<int>([1, 2, 3])));
    }

    [Fact]
    [RequiresDynamicCode("Exercises collection contracts discovered from runtime types.")]
    [RequiresUnreferencedCode("Exercises collection contracts discovered from runtime types.")]
    public void Dynamic_collection_adapters_preserve_automatic_discovery_for_non_aot_hosts()
    {
        var sequence = new ReadOnlySequence<int>([1, 2, 3]);
        var map = new ReadOnlyMap<string, int>(new Dictionary<string, int> { ["a"] = 1 });

        Assert.True(ExprDynamicCollections.TryAsArray(sequence, out IExprArray? array));
        Assert.True(ExprDynamicCollections.TryAsMap(map, out IExprMap? adaptedMap));
        Assert.Equal(2, ExprValue.FetchIndex(array, 1));
        Assert.True(ExprValue.In("a", adaptedMap));
    }

    [Fact]
    public void Generic_collection_adapters_are_live_read_only_views()
    {
        var sequence = new List<int> { 1 };
        var dictionary = new Dictionary<string, int> { ["a"] = 1 };
        ExprReadOnlyListAdapter<int> arrayAdapter = ExprCollections.AsArray(sequence);
        ExprReadOnlyDictionaryAdapter<string, int> mapAdapter = ExprCollections.AsMap(dictionary);

        sequence.Add(2);
        dictionary["a"] = 3;

        Assert.Equal(2, arrayAdapter.Count);
        Assert.Equal(2, arrayAdapter[1]);
        Assert.True(mapAdapter.TryGetValue("a", out object? value));
        Assert.Equal(3, value);
    }

    private sealed class ReadOnlySequence<T>(IReadOnlyList<T> values) : IReadOnlyList<T>
    {
        public int Count => values.Count;

        public T this[int index] => values[index];

        public IEnumerator<T> GetEnumerator() => values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ReadOnlyMap<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> values)
        : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        public IEnumerable<TKey> Keys => values.Keys;

        public IEnumerable<TValue> Values => values.Values;

        public int Count => values.Count;

        public TValue this[TKey key] => values[key];

        public bool ContainsKey(TKey key) => values.ContainsKey(key);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => values.GetEnumerator();

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => values.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
