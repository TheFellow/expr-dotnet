using System;
using Expr.Builtins;
using Expr.Runtime;
using Xunit;

// Inline arrays keep each upstream-derived assertion self-contained and are not used on a hot path.
#pragma warning disable CA1861

namespace Expr.Tests.Builtins;

public sealed class BuiltinValueTests
{
    private readonly ExprBuiltinLibrary library = new();

    [Fact]
    public void Scalar_functions_preserve_expr_numeric_semantics()
    {
        Assert.Equal(5L, Invoke("abs", -5L));
        Assert.Equal(6D, Invoke("ceil", 5.5D));
        Assert.Equal(-6D, Invoke("round", -5.5D));
        Assert.Equal(5L, Invoke("int", "5"));
        Assert.Equal(5.5D, Invoke("float", "5.5"));
        Assert.Equal("true", Invoke("string", true));
        Assert.Equal(long.MinValue, Invoke("abs", long.MinValue));
    }

    [Fact]
    public void Len_counts_unicode_scalars_not_utf16_units_or_utf8_bytes()
    {
        Assert.Equal(3L, Invoke("len", "A😀é"));
        Assert.Equal(3L, Invoke("len", new[] { 1, 2, 3 }));
        Assert.Equal(2L, Invoke("len", new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
    }

    [Fact]
    public void String_search_returns_go_utf8_byte_offsets()
    {
        Assert.Equal(3L, Invoke("indexOf", "éx😀é", "😀"));
        Assert.Equal(7L, Invoke("lastIndexOf", "éx😀é", "é"));
        Assert.Equal(-1L, Invoke("indexOf", "abc", "z"));
    }

    [Fact]
    public void Split_replace_trim_and_case_cover_go_edge_rules()
    {
        Assert.Equal(new[] { "a,", "b,c" }, Assert.IsType<string[]>(Invoke("splitAfter", "a,b,c", ",", 2L)));
        Assert.Equal(new[] { "😀", "é" }, Assert.IsType<string[]>(Invoke("split", "😀é", "")));
        Assert.Equal("-a-b-", Invoke("replace", "ab", "", "-", -1L));
        Assert.Equal("hello", Invoke("trim", "__hello___", "_"));
        Assert.Equal("FOO", Invoke("upper", "foo"));
        Assert.Equal("foo", Invoke("lower", "FOO"));
    }

    [Fact]
    public void Repeat_reports_utf8_memory_and_rejects_hostile_counts()
    {
        ExprInvocationResult result = library.Get("repeat").Invoke(["é", 3L]);

        Assert.Equal("ééé", result.Value);
        Assert.Equal(6UL, result.MemoryCost);
        Assert.Throws<ExprRuntimeException>(() => library.Get("repeat").Invoke(["x", -1L]));
        Assert.Throws<ExprRuntimeException>(() => library.Get("repeat").Invoke(["x", 1_000_001L]));
        Assert.Throws<ExprRuntimeException>(() => library.Get("repeat").Invoke(["é", 600_000L]));
    }

    [Theory]
    [InlineData("bitand", 99L)]
    [InlineData("bitor", -33L)]
    [InlineData("bitxor", -222L)]
    public void Binary_bit_functions_use_signed_64_bit_expr_integers(string name, long expected)
    {
        long left = name == "bitand" ? -157L : name == "bitor" ? 987L : -157L;
        long right = name == "bitand" ? 255L : name == "bitor" ? -123L : 65L;

        Assert.Equal(expected, Invoke(name, left, right));
    }

    [Fact]
    public void Shift_functions_match_go_for_sign_and_large_counts()
    {
        Assert.Equal(312L, Invoke("bitshl", 39L, 3L));
        Assert.Equal(2L, Invoke("bitshr", 5L, 1L));
        Assert.Equal(4_611_686_018_427_387_902L, Invoke("bitushr", -5L, 2L));
        Assert.Equal(0L, Invoke("bitshl", 1L, 64L));
        Assert.Throws<ExprRuntimeException>(() => Invoke("bitshr", -5L, -2L));
    }

    [Fact]
    public void Invalid_conversions_have_expr_runtime_failures()
    {
        Assert.Throws<ExprRuntimeException>(() => Invoke("int", "not-an-int"));
        Assert.Throws<ExprRuntimeException>(() => Invoke("float", "not-a-float"));
        Assert.Throws<ExprRuntimeException>(() => Invoke("abs", "5"));
        Assert.Throws<ExprRuntimeException>(() => Invoke("bitnot", "1"));
    }

    private object? Invoke(string name, params object?[] arguments) => library.Get(name).Invoke(arguments).Value;
}
#pragma warning restore CA1861
