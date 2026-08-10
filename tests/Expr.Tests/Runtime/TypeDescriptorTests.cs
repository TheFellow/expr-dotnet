using System;
using System.Collections.Generic;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Runtime;

public sealed class TypeDescriptorTests
{
    [Fact]
    public void Any_is_a_symmetric_type_wildcard()
    {
        Assert.True(ExprTypes.Any.IsEquivalentTo(ExprTypes.Integer));
        Assert.True(ExprTypes.Integer.IsEquivalentTo(ExprTypes.Any));
        Assert.False(ExprTypes.Integer.IsEquivalentTo(ExprTypes.Float));
    }

    [Fact]
    public void Array_equivalence_uses_element_semantics()
    {
        var integers = new ArrayTypeDescriptor(ExprTypes.Integer);
        var moreIntegers = new ArrayTypeDescriptor(ExprTypes.Integer);
        var floats = new ArrayTypeDescriptor(ExprTypes.Float);

        Assert.True(integers.IsEquivalentTo(moreIntegers));
        Assert.False(integers.IsEquivalentTo(floats));
        Assert.Equal("Array{int}", integers.ToString());
    }

    [Fact]
    public void Map_equivalence_checks_fields_and_extra_value_type()
    {
        var first = new MapTypeDescriptor(
            [new KeyValuePair<string, ExprTypeDescriptor>("name", ExprTypes.String)],
            ExprTypes.Integer);
        var same = new MapTypeDescriptor(
            [new KeyValuePair<string, ExprTypeDescriptor>("name", ExprTypes.String)],
            ExprTypes.Integer);
        var strict = new MapTypeDescriptor(
            [new KeyValuePair<string, ExprTypeDescriptor>("name", ExprTypes.String)]);

        Assert.True(first.IsEquivalentTo(same));
        Assert.False(first.IsEquivalentTo(strict));
        Assert.True(first.TryGetField("other", out ExprTypeDescriptor? other));
        Assert.Same(ExprTypes.Integer, other);
        Assert.False(strict.TryGetField("other", out _));
    }

    [Theory]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(nint))]
    [InlineData(typeof(nuint))]
    public void Clr_integrals_map_to_the_expr_integer_family(Type clrType)
    {
        Assert.Same(ExprTypes.Integer, ExprTypes.FromClrType(clrType));
    }

    [Fact]
    public void Clr_collections_and_temporal_values_map_to_expr_types()
    {
        var array = Assert.IsType<ArrayTypeDescriptor>(ExprTypes.FromClrType<int[]>());
        var map = Assert.IsType<MapTypeDescriptor>(ExprTypes.FromClrType<Dictionary<string, double>>());
        var integerMap = Assert.IsType<MapTypeDescriptor>(ExprTypes.FromClrType<Dictionary<int, string>>());

        Assert.Same(ExprTypes.Integer, array.ElementType);
        Assert.Same(ExprTypes.Float, map.AdditionalValueType);
        Assert.Same(ExprTypes.String, map.KeyType);
        Assert.Same(ExprTypes.Integer, integerMap.KeyType);
        Assert.False(integerMap.TryGetField("not-an-integer", out _));
        Assert.Same(ExprTypes.Time, ExprTypes.FromClrType<DateTimeOffset>());
        Assert.Same(ExprTypes.Duration, ExprTypes.FromClrType<TimeSpan>());
    }
}
