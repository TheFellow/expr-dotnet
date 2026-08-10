using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Expr.Runtime;

namespace Expr.Builtins;

internal static class ExprBuiltinTime
{
    private static readonly string[] DefaultLayouts =
    [
        "2006-01-02",
        "15:04:05",
        "2006-01-02 15:04:05",
        "2006-01-02T15:04:05Z07:00",
        "02 Jan 06 15:04 MST",
        "Monday, 02-Jan-06 15:04:05 MST",
        "Mon, 02 Jan 2006 15:04:05 MST",
    ];

    private static readonly (string Token, string Format)[] LayoutTokens =
    [
        ("January", "MMMM"),
        ("Monday", "dddd"),
        ("2006", "yyyy"),
        ("Jan", "MMM"),
        ("Mon", "ddd"),
        ("Z07:00", "K"),
        ("-07:00", "zzz"),
        ("-0700", "zzz"),
        ("MST", "zzz"),
        (".000000000", ".fffffff"),
        (".000000", ".ffffff"),
        (".000", ".fff"),
        ("15", "HH"),
        ("03", "hh"),
        ("04", "mm"),
        ("05", "ss"),
        ("02", "dd"),
        ("01", "MM"),
        ("06", "yy"),
        ("PM", "tt"),
        ("pm", "tt"),
        ("3", "h"),
        ("2", "d"),
        ("1", "M"),
    ];

    public static object Now(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        DateTimeOffset now = options.TimeProvider.GetUtcNow();
        if (arguments.Length == 0)
        {
            return TimeZoneInfo.ConvertTime(now, options.TimeZone);
        }

        if (arguments[0] is TimeZoneInfo timezone)
        {
            return TimeZoneInfo.ConvertTime(now, timezone);
        }

        throw new ExprRuntimeException(
            $"invalid number of arguments (expected 0, got {arguments.Length})");
    }

    public static object Duration(ReadOnlySpan<object?> arguments)
    {
        string text = ExprBuiltinStrings.RequireString(arguments[0], "duration");
        if (!TryParseDuration(text, out TimeSpan duration))
        {
            throw new ExprRuntimeException($"invalid duration {text}");
        }

        return duration;
    }

    public static object Date(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        int offset = 0;
        TimeZoneInfo timezone = options.TimeZone;
        if (arguments.Length > 0 && arguments[0] is TimeZoneInfo suppliedTimezone)
        {
            timezone = suppliedTimezone;
            offset = 1;
        }

        int remaining = arguments.Length - offset;
        if (remaining is < 1 or > 3)
        {
            throw new ExprRuntimeException(
                $"invalid number of arguments (expected at least 1 and at most 3, got {remaining})");
        }

        string text = ExprBuiltinStrings.RequireString(arguments[offset], "date");
        if (remaining == 3)
        {
            timezone = LoadTimezone(ExprBuiltinStrings.RequireString(arguments[offset + 2], "date"));
        }

        if (remaining >= 2)
        {
            string layout = ExprBuiltinStrings.RequireString(arguments[offset + 1], "date");
            if (TryParseDate(text, layout, timezone, out DateTimeOffset parsed))
            {
                return parsed;
            }

            throw new ExprRuntimeException($"invalid date {text}");
        }

        foreach (string layout in DefaultLayouts)
        {
            if (TryParseDate(text, layout, timezone, out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        throw new ExprRuntimeException($"invalid date {text}");
    }

    public static object Timezone(ReadOnlySpan<object?> arguments) =>
        LoadTimezone(ExprBuiltinStrings.RequireString(arguments[0], "timezone"));

    private static bool TryParseDuration(string text, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int index = 0;
        int sign = 1;
        if (text[0] is '+' or '-')
        {
            sign = text[0] == '-' ? -1 : 1;
            index++;
        }

        if (index == text.Length)
        {
            return false;
        }

        if (text.AsSpan(index).SequenceEqual("0"))
        {
            result = TimeSpan.Zero;
            return true;
        }

        decimal ticks = 0;
        bool found = false;
        while (index < text.Length)
        {
            int numberStart = index;
            bool dot = false;
            while (index < text.Length && (char.IsAsciiDigit(text[index]) || text[index] == '.'))
            {
                if (text[index] == '.' && dot)
                {
                    return false;
                }

                dot |= text[index] == '.';
                index++;
            }

            if (numberStart == index ||
                !decimal.TryParse(
                    text.AsSpan(numberStart, index - numberStart),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out decimal amount))
            {
                return false;
            }

            decimal ticksPerUnit;
            if (Consume(text, ref index, "ns"))
            {
                ticksPerUnit = 0.01M;
            }
            else if (Consume(text, ref index, "us") || Consume(text, ref index, "µs") || Consume(text, ref index, "μs"))
            {
                ticksPerUnit = 10M;
            }
            else if (Consume(text, ref index, "ms"))
            {
                ticksPerUnit = TimeSpan.TicksPerMillisecond;
            }
            else if (Consume(text, ref index, "s"))
            {
                ticksPerUnit = TimeSpan.TicksPerSecond;
            }
            else if (Consume(text, ref index, "m"))
            {
                ticksPerUnit = TimeSpan.TicksPerMinute;
            }
            else if (Consume(text, ref index, "h"))
            {
                ticksPerUnit = TimeSpan.TicksPerHour;
            }
            else
            {
                return false;
            }

            try
            {
                ticks = checked(ticks + (amount * ticksPerUnit));
            }
            catch (OverflowException)
            {
                return false;
            }

            found = true;
        }

        try
        {
            result = TimeSpan.FromTicks(checked((long)decimal.Truncate(ticks * sign)));
            return found;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseDate(
        string originalText,
        string goLayout,
        TimeZoneInfo timezone,
        out DateTimeOffset result)
    {
        string text = originalText;
        string format = ConvertLayout(goLayout);
        bool hasNumericOffset = goLayout.Contains("-0700", StringComparison.Ordinal) ||
            goLayout.Contains("-07:00", StringComparison.Ordinal) ||
            goLayout.Contains("Z07:00", StringComparison.Ordinal);
        if (goLayout.Contains("-0700", StringComparison.Ordinal))
        {
            int signIndex = FindTrailingOffset(text);
            if (signIndex >= 0 && text.Length - signIndex == 5)
            {
                text = string.Concat(text.AsSpan(0, signIndex + 3), ":", text.AsSpan(signIndex + 3));
            }
        }

        if (hasNumericOffset && DateTimeOffset.TryParseExact(
                text,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out result))
        {
            return true;
        }

        string timezoneFreeFormat = format.Replace(" zzz", string.Empty, StringComparison.Ordinal);
        string timezoneFreeText = text;
        if (goLayout.Contains("MST", StringComparison.Ordinal))
        {
            int lastSpace = text.LastIndexOf(' ');
            if (lastSpace >= 0)
            {
                timezoneFreeText = text[..lastSpace];
            }
        }

        if (!DateTime.TryParseExact(
                timezoneFreeText,
                timezoneFreeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime local))
        {
            result = default;
            return false;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        result = new DateTimeOffset(local, timezone.GetUtcOffset(local));
        return true;
    }

    private static string ConvertLayout(string layout)
    {
        var result = new StringBuilder(layout.Length * 2);
        int index = 0;
        while (index < layout.Length)
        {
            bool replaced = false;
            foreach ((string token, string format) in LayoutTokens)
            {
                if (layout.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
                {
                    result.Append(format);
                    index += token.Length;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                char current = layout[index++];
                if (char.IsLetter(current) || current is '\'' or '\\')
                {
                    result.Append('\\');
                }

                result.Append(current);
            }
        }

        return result.ToString();
    }

    private static TimeZoneInfo LoadTimezone(string name)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ExprRuntimeException($"unknown time zone {name}", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ExprRuntimeException($"unknown time zone {name}", exception);
        }
    }

    private static bool Consume(string text, ref int index, string token)
    {
        if (!text.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
        {
            return false;
        }

        index += token.Length;
        return true;
    }

    private static int FindTrailingOffset(string text)
    {
        for (int index = text.Length - 5; index >= 0; index--)
        {
            if (text[index] is '+' or '-')
            {
                return index;
            }
        }

        return -1;
    }
}
