using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Builtins;
using Expr.Runtime;
using Xunit;

namespace Expr.Tests.Builtins;

public sealed class BuiltinSerializationAndTimeTests
{
    [Fact]
    public void Json_matches_go_indentation_key_order_and_float_decoding()
    {
        var library = new ExprBuiltinLibrary();
        var map = new Dictionary<string, object?> { ["foo"] = 1L, ["bar"] = 2L };

        ExprInvocationResult encoded = library.Get("toJSON").Invoke([map]);
        Assert.Equal("{\n  \"bar\": 2,\n  \"foo\": 1\n}", encoded.Value);
        Assert.True(encoded.MemoryCost > 0);
        IExprArray decoded = Assert.IsAssignableFrom<IExprArray>(library.Get("fromJSON").Invoke(["[1, 2, 3]"]).Value);
        Assert.Equal([1D, 2D, 3D], decoded.ToArray());
        IExprMap duplicate = Assert.IsAssignableFrom<IExprMap>(
            library.Get("fromJSON").Invoke(["{\"a\": 1, \"a\": 2}"]).Value);
        Assert.True(duplicate.TryGetValue("a", out object? duplicateValue));
        Assert.Equal(2D, duplicateValue);
    }

    [Fact]
    public void Json_rejects_non_finite_numbers_cycles_and_excessive_input()
    {
        var library = new ExprBuiltinLibrary(new ExprBuiltinOptions { MaximumAllocation = 32 });
        var cycle = new object?[1];
        cycle[0] = cycle;

        Assert.Throws<ExprRuntimeException>(() => library.Get("toJSON").Invoke([cycle]));
        Assert.Throws<ExprRuntimeException>(() => library.Get("fromJSON").Invoke(["5e2482"]));
        Assert.Throws<ExprRuntimeException>(() => library.Get("fromJSON").Invoke([new string(' ', 40) + "null"]));
    }

    [Fact]
    public void Base64_is_utf8_and_reports_allocated_size()
    {
        var library = new ExprBuiltinLibrary();

        ExprInvocationResult encoded = library.Get("toBase64").Invoke(["héllo"]);
        Assert.Equal("aMOpbGxv", encoded.Value);
        Assert.Equal(8UL, encoded.MemoryCost);
        ExprInvocationResult decoded = library.Get("fromBase64").Invoke([encoded.Value]);
        Assert.Equal("héllo", decoded.Value);
        Assert.Equal(6UL, decoded.MemoryCost);
        Assert.Throws<ExprRuntimeException>(() => library.Get("fromBase64").Invoke(["***"]));
    }

    [Theory]
    [InlineData("1h", 3_600_000L)]
    [InlineData("1h30m", 5_400_000L)]
    [InlineData("-1.5s", -1_500L)]
    [InlineData("250us", 0.25D)]
    [InlineData("0", 0D)]
    public void Duration_parses_go_units(string text, double expectedMilliseconds)
    {
        var library = new ExprBuiltinLibrary();

        TimeSpan duration = Assert.IsType<TimeSpan>(library.Get("duration").Invoke([text]).Value);
        Assert.Equal(expectedMilliseconds, duration.TotalMilliseconds, 8);
    }

    [Fact]
    public void Duration_rejects_invalid_and_overflowing_values()
    {
        var library = new ExprBuiltinLibrary();

        Assert.Throws<ExprRuntimeException>(() => library.Get("duration").Invoke(["error"]));
        Assert.Throws<ExprRuntimeException>(() => library.Get("duration").Invoke(["999999999999999999999h"]));
    }

    [Fact]
    public void Now_uses_injected_clock_and_timezone()
    {
        var instant = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var options = new ExprBuiltinOptions
        {
            TimeProvider = new FixedTimeProvider(instant),
            TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/+02", TimeSpan.FromHours(2), "Test", "Test"),
        };
        var library = new ExprBuiltinLibrary(options);

        Assert.Equal(instant.ToOffset(TimeSpan.FromHours(2)), library.Get("now").Invoke([]).Value);
        Assert.Equal(instant, library.Get("now").Invoke([TimeZoneInfo.Utc]).Value);
    }

    [Fact]
    public void Date_supports_default_and_go_reference_layouts()
    {
        var library = new ExprBuiltinLibrary();

        Assert.Equal(
            new DateTimeOffset(2017, 10, 23, 0, 0, 0, TimeSpan.Zero),
            library.Get("date").Invoke(["2017-10-23"]).Value);
        Assert.Equal(
            new DateTimeOffset(2006, 1, 2, 0, 0, 0, TimeSpan.Zero),
            library.Get("date").Invoke(["2006.01.02", "2006.01.02"]).Value);
        Assert.Equal(
            new DateTimeOffset(2023, 4, 23, 0, 30, 0, TimeSpan.FromHours(1)),
            library.Get("date").Invoke(["2023-04-23T00:30:00.000+0100", "2006-01-02T15:04:05.000-0700", "UTC"]).Value);
        Assert.Throws<ExprRuntimeException>(() => library.Get("date").Invoke(["error"]));
    }

    [Fact]
    public void Timezone_uses_the_platform_tzdb_mapping()
    {
        var library = new ExprBuiltinLibrary();

        Assert.Equal(TimeZoneInfo.Utc, library.Get("timezone").Invoke(["UTC"]).Value);
        Assert.Throws<ExprRuntimeException>(() => library.Get("timezone").Invoke(["Etc/Definitely-Unknown"]));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
