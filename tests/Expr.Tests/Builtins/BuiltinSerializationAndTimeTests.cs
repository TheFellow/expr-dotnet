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
    public void Json_matches_go_time_duration_struct_and_escaping_rules()
    {
        var library = new ExprBuiltinLibrary();
        var value = new JsonFixture
        {
            Duration = TimeSpan.FromTicks(12_345),
            Text = "<line>\né",
            When = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero).AddTicks(1_234_567),
        };

        Assert.Equal(
            "{\n  \"Duration\": 1234500,\n  \"Text\": \"\\u003cline\\u003e\\né\",\n  \"When\": \"2024-02-03T04:05:06.1234567Z\"\n}",
            library.Get("toJSON").Invoke([value]).Value);
    }

    [Fact]
    public void Json_encodes_byte_arrays_as_go_base64_strings()
    {
        var library = new ExprBuiltinLibrary();

        Assert.Equal("\"YWJj\"", library.Get("toJSON").Invoke(["abc"u8.ToArray()]).Value);
        Assert.Equal(
            "\"YWJj\"",
            library.Get("toJSON").Invoke([new ReadOnlyMemory<byte>("abc"u8.ToArray())]).Value);
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
        Assert.Throws<ExprRuntimeException>(() => library.Get("toJSON").Invoke([double.NaN]));
        Assert.Throws<ExprRuntimeException>(() => library.Get("toJSON").Invoke([TimeSpan.MaxValue]));
    }

    [Fact]
    public void Json_accounts_for_host_members_before_materializing_output()
    {
        var library = new ExprBuiltinLibrary(new ExprBuiltinOptions { MaximumAllocation = 32 });

        Assert.Throws<ExprRuntimeException>(() =>
            library.Get("toJSON").Invoke([new HostStringFixture { Value = new string('x', 40) }]));
        Assert.Throws<ExprRuntimeException>(() =>
            library.Get("fromJSON").Invoke(["{\"value\":\"abcdefghijklmnopqrstuvwxyz\"}"]));
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

    [Theory]
    [InlineData("2023-04-23T00:30:00Z", "2006-01-02T15:04:05Z07", 0)]
    [InlineData("2023-04-23T00:30:00+01", "2006-01-02T15:04:05-07", 60)]
    [InlineData("2023-04-23T00:30:00+0130", "2006-01-02T15:04:05-0700", 90)]
    [InlineData("2023-04-23T00:30:00+01:30", "2006-01-02T15:04:05Z07:00", 90)]
    [InlineData("2023-04-23T00:30:00+013000", "2006-01-02T15:04:05Z070000", 90)]
    [InlineData("2023-04-23T00:30:00+01:30:00", "2006-01-02T15:04:05-07:00:00", 90)]
    public void Date_supports_all_go_numeric_zone_token_families(
        string text,
        string layout,
        int expectedOffsetMinutes)
    {
        var library = new ExprBuiltinLibrary();

        var parsed = Assert.IsType<DateTimeOffset>(library.Get("date").Invoke([text, layout]).Value);

        Assert.Equal(TimeSpan.FromMinutes(expectedOffsetMinutes), parsed.Offset);
    }

    [Fact]
    public void Date_preserves_instant_for_go_second_precision_offsets()
    {
        var library = new ExprBuiltinLibrary();

        var parsed = Assert.IsType<DateTimeOffset>(library.Get("date").Invoke(
            ["2023-04-23T00:30:00+01:30:45", "2006-01-02T15:04:05Z07:00:00"]).Value);

        Assert.Equal(new DateTimeOffset(2023, 4, 22, 22, 59, 15, TimeSpan.Zero), parsed);
    }

    [Theory]
    [InlineData("2024-02-03 04:05:06.1", "2006-01-02 15:04:05.9", 1_000_000)]
    [InlineData("2024-02-03 04:05:06.123456789", "2006-01-02 15:04:05.999999999", 1_234_567)]
    [InlineData("2024-02-03 04:05:06,120000000", "2006-01-02 15:04:05,000000000", 1_200_000)]
    [InlineData("2024-02-03 04:05:06.25", "2006-01-02 15:04:05", 2_500_000)]
    [InlineData("2024-02-03 04:05:6.25", "2006-01-02 15:04:5", 2_500_000)]
    [InlineData("2024-02-03 04:05:06", "2006-01-02 15:04:05.999", 0)]
    [InlineData("2024-02-03 04:05:06.123456789123", "2006-01-02 15:04:05.9", 1_234_567)]
    public void Date_supports_go_fraction_grammar_and_truncates_only_sub_tick_precision(
        string text,
        string layout,
        int expectedTicks)
    {
        var library = new ExprBuiltinLibrary();

        var parsed = Assert.IsType<DateTimeOffset>(library.Get("date").Invoke([text, layout]).Value);

        Assert.Equal(expectedTicks, parsed.Ticks % TimeSpan.TicksPerSecond);
    }

    [Theory]
    [InlineData("2024-060", "2006-002", 2, 29)]
    [InlineData("2024- 60", "2006-__2", 2, 29)]
    [InlineData("2024- 3", "2006-_2", 1, 3)]
    [InlineData("2024-3", "2006-_2", 1, 3)]
    [InlineData("2024-  3", "2006-__2", 1, 3)]
    [InlineData("2024-123", "2006-__2", 5, 2)]
    [InlineData("2024-Feb- 3 03:04:05 PM", "2006-Jan-_2 03:04:05 PM", 2, 3)]
    [InlineData("Sunday, February 3 3:4:5 pm 2024", "Monday, January 2 3:4:5 pm 2006", 2, 3)]
    public void Date_supports_go_names_padding_ordinal_days_and_meridiem(
        string text,
        string layout,
        int expectedMonth,
        int expectedDay)
    {
        ArgumentNullException.ThrowIfNull(text);
        var library = new ExprBuiltinLibrary();

        var parsed = Assert.IsType<DateTimeOffset>(library.Get("date").Invoke([text, layout]).Value);

        Assert.Equal(expectedMonth, parsed.Month);
        Assert.Equal(expectedDay, parsed.Day);
        if (text.Contains("PM", StringComparison.Ordinal) || text.Contains("pm", StringComparison.Ordinal))
        {
            Assert.Equal(15, parsed.Hour);
        }
    }

    [Theory]
    [InlineData("february  3 2024", "January 2 2006", 0)]
    [InlineData("sUNDAY feb 3 2024", "Monday Jan 2 2006", 0)]
    [InlineData("00:04", "03:04", 0)]
    [InlineData("12:04", "03:04", 12)]
    [InlineData("12:04 am", "03:04 pm", 0)]
    [InlineData("00:04 pm", "03:04 pm", 12)]
    public void Date_matches_go_case_space_and_twelve_hour_rules(
        string text,
        string layout,
        int expectedHour)
    {
        var library = new ExprBuiltinLibrary();

        var parsed = Assert.IsType<DateTimeOffset>(library.Get("date").Invoke([text, layout]).Value);

        Assert.Equal(expectedHour, parsed.Hour);
    }

    [Fact]
    public void Date_uses_the_builtin_default_location_when_the_layout_has_no_zone()
    {
        var location = TimeZoneInfo.CreateCustomTimeZone(
            "Test/Default",
            TimeSpan.FromHours(3),
            "Test default",
            "TDT");
        var library = new ExprBuiltinLibrary(new ExprBuiltinOptions { TimeZone = location });

        var parsed = Assert.IsType<DateTimeOffset>(library.Get("date").Invoke(
            ["2024-02-03 04:05", "2006-01-02 15:04"]).Value);

        Assert.Equal(TimeSpan.FromHours(3), parsed.Offset);
    }

    [Fact]
    public void Date_fabricates_unknown_named_zone_and_resolves_configured_zone_abbreviation()
    {
        var library = new ExprBuiltinLibrary();
        var testZone = TimeZoneInfo.CreateCustomTimeZone("Test/Zone", TimeSpan.FromHours(2), "TST", "TST");

        var fabricated = Assert.IsType<DateTimeOffset>(
            library.Get("date").Invoke(["2024-02-03 04:05 XYZ", "2006-01-02 15:04 MST"]).Value);
        var located = Assert.IsType<DateTimeOffset>(
            library.Get("date").Invoke([testZone, "2024-02-03 04:05 TST", "2006-01-02 15:04 MST"]).Value);

        Assert.Equal(TimeSpan.Zero, fabricated.Offset);
        Assert.Equal(TimeSpan.FromHours(2), located.Offset);
    }

    [Theory]
    [InlineData("2024-02-03 04:05 GMT+3", 180)]
    [InlineData("2024-02-03 04:05 ChST", 0)]
    [InlineData("2024-02-03 04:05 WITA", 0)]
    public void Date_accepts_go_special_named_zone_forms(string text, int expectedOffsetMinutes)
    {
        var library = new ExprBuiltinLibrary();

        var parsed = Assert.IsType<DateTimeOffset>(
            library.Get("date").Invoke([text, "2006-01-02 15:04 MST"]).Value);

        Assert.Equal(TimeSpan.FromMinutes(expectedOffsetMinutes), parsed.Offset);
    }

    [Fact]
    public void Date_rejects_conflicting_ordinal_and_calendar_dates()
    {
        var library = new ExprBuiltinLibrary();

        Assert.Throws<ExprRuntimeException>(() =>
            library.Get("date").Invoke(["2024-02-28-060", "2006-01-02-002"]));
    }

    [Theory]
    [InlineData("2024-02-03 24:00:00")]
    [InlineData("2024-02-03 23:60:00")]
    [InlineData("2024-02-03 23:59:60")]
    public void Date_rejects_go_clock_boundary_values(string text)
    {
        var library = new ExprBuiltinLibrary();

        Assert.Throws<ExprRuntimeException>(() =>
            library.Get("date").Invoke([text, "2006-01-02 15:04:05"]));
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

    private sealed class JsonFixture
    {
        public TimeSpan Duration { get; init; }

        public string Text { get; init; } = string.Empty;

        public DateTimeOffset When { get; init; }
    }

    private sealed class HostStringFixture
    {
        public string Value { get; init; } = string.Empty;
    }
}
