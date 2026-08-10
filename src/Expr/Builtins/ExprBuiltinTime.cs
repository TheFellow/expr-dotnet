using System;
using System.Globalization;
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

    public static object Now(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        DateTimeOffset now = options.TimeProvider.GetUtcNow();
        if (arguments.Length == 0)
        {
            return TimeZoneInfo.ConvertTime(now, options.TimeZone);
        }

        if (arguments.Length == 1 && arguments[0] is TimeZoneInfo timezone)
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
        TimeZoneInfo? location = options.TimeZone;
        if (arguments.Length > 0 && arguments[0] is TimeZoneInfo suppliedLocation)
        {
            location = suppliedLocation;
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
            location = LoadTimezone(ExprBuiltinStrings.RequireString(arguments[offset + 2], "date"));
        }

        if (remaining >= 2)
        {
            string layout = ExprBuiltinStrings.RequireString(arguments[offset + 1], "date");
            if (GoTimeLayoutParser.TryParse(text, layout, location, out DateTimeOffset parsed))
            {
                return parsed;
            }

            throw new ExprRuntimeException($"invalid date {text}");
        }

        foreach (string layout in DefaultLayouts)
        {
            if (GoTimeLayoutParser.TryParse(text, layout, location, out DateTimeOffset parsed))
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
}
