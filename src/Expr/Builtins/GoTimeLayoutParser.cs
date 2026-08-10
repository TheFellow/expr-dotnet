using System;
using System.Collections.Generic;
using System.Globalization;

namespace Expr.Builtins;

internal static class GoTimeLayoutParser
{
    private static readonly string[] LongMonths = DateTimeFormatInfo.InvariantInfo.MonthNames;
    private static readonly string[] ShortMonths = DateTimeFormatInfo.InvariantInfo.AbbreviatedMonthNames;
    private static readonly string[] LongWeekdays = DateTimeFormatInfo.InvariantInfo.DayNames;
    private static readonly string[] ShortWeekdays = DateTimeFormatInfo.InvariantInfo.AbbreviatedDayNames;

    private static readonly string[] Tokens =
    [
        "Z07:00:00", "-07:00:00", "Z070000", "-070000", "January", "Monday",
        "Z07:00", "-07:00", "Z0700", "-0700", "2006", "__2", "002",
        "Jan", "Mon", "MST", "Z07", "-07", "PM", "pm", "_2", "06",
        "01", "02", "03", "04", "05", "15", "1", "2", "3", "4", "5",
    ];

    public static bool TryParse(
        string text,
        string layout,
        TimeZoneInfo? location,
        out DateTimeOffset result)
    {
        var state = new ParseState(text, layout);
        while (state.LayoutIndex < layout.Length)
        {
            if (TryFractionToken(ref state, out bool recognizedFraction))
            {
                continue;
            }

            if (recognizedFraction)
            {
                result = default;
                return false;
            }

            string? token = FindToken(layout, state.LayoutIndex);
            if (token is null)
            {
                if (!state.ConsumeLiteral())
                {
                    result = default;
                    return false;
                }

                continue;
            }

            state.LayoutIndex += token.Length;
            if (!ConsumeToken(token, ref state))
            {
                result = default;
                return false;
            }

            if (token is "5" or "05" &&
                !IsFractionToken(layout, state.LayoutIndex) &&
                !state.HasFraction && state.TextIndex < text.Length &&
                text[state.TextIndex] is '.' or ',')
            {
                _ = ConsumeImplicitFraction(ref state);
            }
        }

        if (state.TextIndex != text.Length || !state.ApplyOrdinalDay())
        {
            result = default;
            return false;
        }

        int hour = state.Hour;
        if (state.UsesTwelveHour)
        {
            if (hour is < 0 or > 12)
            {
                result = default;
                return false;
            }

            if (state.Meridiem is false && hour == 12)
            {
                hour = 0;
            }
            else if (state.Meridiem is true && hour < 12)
            {
                hour += 12;
            }
        }

        try
        {
            // Go supports year zero. DateTimeOffset starts at year one, which is the documented host mapping.
            int year = Math.Max(1, state.Year);
            if (hour >= 24 || state.Minute >= 60 || state.Second >= 60)
            {
                result = default;
                return false;
            }

            var local = new DateTime(year, state.Month, state.Day, 0, 0, 0, DateTimeKind.Unspecified)
                .AddHours(hour)
                .AddMinutes(state.Minute)
                .AddSeconds(state.Second)
                .AddTicks(state.FractionNanoseconds / 100);
            TimeSpan zoneOffset = state.ZoneOffset ?? ResolveNamedZone(state.ZoneName, local, location);
            if (zoneOffset.Ticks % TimeSpan.TicksPerMinute != 0 ||
                zoneOffset < TimeSpan.FromHours(-14) || zoneOffset > TimeSpan.FromHours(14))
            {
                result = new DateTimeOffset(DateTime.SpecifyKind(local - zoneOffset, DateTimeKind.Utc));
                return true;
            }

            result = new DateTimeOffset(local, zoneOffset);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = default;
            return false;
        }
    }

    private static bool ConsumeToken(string token, ref ParseState state) => token switch
    {
        "2006" => state.ConsumeNumber(4, 4, out state.Year),
        "06" => state.ConsumeTwoDigitYear(),
        "January" => state.ConsumeMonthName(LongMonths),
        "Jan" => state.ConsumeMonthName(ShortMonths),
        "01" => state.ConsumeMonthNumber(2, 2),
        "1" => state.ConsumeMonthNumber(1, 2),
        "02" => state.ConsumeDayNumber(2, 2),
        "_2" => state.ConsumePaddedDay(2),
        "2" => state.ConsumeDayNumber(1, 2),
        "002" => state.ConsumeNumber(3, 3, out state.OrdinalDay),
        "__2" => state.ConsumeSpacePaddedNumber(3, out state.OrdinalDay),
        "15" => state.ConsumeNumber(2, 2, out state.Hour),
        "03" => state.ConsumeTwelveHour(2, 2),
        "3" => state.ConsumeTwelveHour(1, 2),
        "04" => state.ConsumeNumber(2, 2, out state.Minute),
        "4" => state.ConsumeNumber(1, 2, out state.Minute),
        "05" => state.ConsumeNumber(2, 2, out state.Second),
        "5" => state.ConsumeNumber(1, 2, out state.Second),
        "PM" => state.ConsumeMeridiem(lowercase: false),
        "pm" => state.ConsumeMeridiem(lowercase: true),
        "Monday" => state.ConsumeName(LongWeekdays, out _),
        "Mon" => state.ConsumeName(ShortWeekdays, out _),
        "MST" => state.ConsumeZoneName(),
        "Z07" or "-07" => state.ConsumeNumericZone(token[0] == 'Z', hasMinutes: false, hasSeconds: false, colon: false),
        "Z0700" or "-0700" => state.ConsumeNumericZone(token[0] == 'Z', hasMinutes: true, hasSeconds: false, colon: false),
        "Z07:00" or "-07:00" => state.ConsumeNumericZone(token[0] == 'Z', hasMinutes: true, hasSeconds: false, colon: true),
        "Z070000" or "-070000" => state.ConsumeNumericZone(token[0] == 'Z', hasMinutes: true, hasSeconds: true, colon: false),
        "Z07:00:00" or "-07:00:00" => state.ConsumeNumericZone(token[0] == 'Z', hasMinutes: true, hasSeconds: true, colon: true),
        _ => false,
    };

    private static string? FindToken(string layout, int index)
    {
        foreach (string token in Tokens)
        {
            if (layout.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    private static bool TryFractionToken(ref ParseState state, out bool recognized)
    {
        char separator = state.Layout[state.LayoutIndex];
        if (separator is not ('.' or ',') || state.LayoutIndex + 1 >= state.Layout.Length)
        {
            recognized = false;
            return false;
        }

        char digit = state.Layout[state.LayoutIndex + 1];
        if (digit is not ('0' or '9'))
        {
            recognized = false;
            return false;
        }

        int end = state.LayoutIndex + 1;
        while (end < state.Layout.Length && state.Layout[end] == digit && end - state.LayoutIndex <= 9)
        {
            end++;
        }

        int digits = end - state.LayoutIndex - 1;
        if (digits is < 1 or > 9 || (end < state.Layout.Length && state.Layout[end] == digit))
        {
            recognized = false;
            return false;
        }

        recognized = true;
        state.LayoutIndex = end;
        return state.ConsumeFraction(separator, digits, exact: digit == '0');
    }

    private static bool IsFractionToken(string layout, int index) =>
        index + 1 < layout.Length &&
        layout[index] is '.' or ',' &&
        layout[index + 1] is '0' or '9';

    private static bool ConsumeImplicitFraction(ref ParseState state)
    {
        char separator = state.Text[state.TextIndex];
        return state.ConsumeFraction(separator, 9, exact: false);
    }

    private static TimeSpan ResolveNamedZone(string? zoneName, DateTime local, TimeZoneInfo? location)
    {
        if (zoneName is null)
        {
            return location?.GetUtcOffset(local) ?? TimeSpan.Zero;
        }

        if (zoneName is "UTC" or "GMT")
        {
            return TimeSpan.Zero;
        }

        if (zoneName.StartsWith("GMT", StringComparison.Ordinal) &&
            int.TryParse(zoneName.AsSpan(3), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int hours) &&
            hours is >= -23 and <= 23 and not 0)
        {
            return TimeSpan.FromHours(hours);
        }

        if (location is not null && MatchesLocationName(zoneName, location, local))
        {
            return location.GetUtcOffset(local);
        }

        // time.Parse fabricates an otherwise unknown abbreviation with a zero offset.
        return TimeSpan.Zero;
    }

    private static bool MatchesLocationName(string name, TimeZoneInfo location, DateTime local)
    {
        string displayName = location.IsDaylightSavingTime(local) ? location.DaylightName : location.StandardName;
        return string.Equals(name, location.Id, StringComparison.Ordinal) ||
            string.Equals(name, displayName, StringComparison.Ordinal) ||
            string.Equals(name, Initials(displayName), StringComparison.Ordinal);
    }

    private static string Initials(string value)
    {
        Span<char> initials = stackalloc char[Math.Min(value.Length, 8)];
        int length = 0;
        bool atWordStart = true;
        foreach (char character in value)
        {
            if (char.IsLetter(character) && atWordStart)
            {
                if (length == initials.Length)
                {
                    return string.Empty;
                }

                initials[length++] = char.ToUpperInvariant(character);
            }

            atWordStart = !char.IsLetter(character);
        }

        return new string(initials[..length]);
    }

    private ref struct ParseState(string text, string layout)
    {
        public readonly string Text = text;
        public readonly string Layout = layout;
        public int TextIndex;
        public int LayoutIndex;
        public int Year;
        public int Month = 1;
        public int Day = 1;
        public int OrdinalDay;
        public bool MonthSpecified;
        public bool DaySpecified;
        public int Hour;
        public int Minute;
        public int Second;
        public int FractionNanoseconds;
        public bool HasFraction;
        public bool UsesTwelveHour;
        public bool? Meridiem;
        public TimeSpan? ZoneOffset;
        public string? ZoneName;

        public bool ConsumeLiteral()
        {
            char expected = Layout[LayoutIndex++];
            if (expected == ' ')
            {
                while (LayoutIndex < Layout.Length && Layout[LayoutIndex] == ' ')
                {
                    LayoutIndex++;
                }

                if (TextIndex >= Text.Length || Text[TextIndex] != ' ')
                {
                    return false;
                }

                while (TextIndex < Text.Length && Text[TextIndex] == ' ')
                {
                    TextIndex++;
                }

                return true;
            }

            if (TextIndex >= Text.Length || Text[TextIndex] != expected)
            {
                return false;
            }

            TextIndex++;
            return true;
        }

        public bool ConsumeNumber(int minimumDigits, int maximumDigits, out int value)
        {
            int start = TextIndex;
            while (TextIndex < Text.Length && TextIndex - start < maximumDigits && char.IsAsciiDigit(Text[TextIndex]))
            {
                TextIndex++;
            }

            if (TextIndex - start < minimumDigits || !int.TryParse(
                    Text.AsSpan(start, TextIndex - start),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                TextIndex = start;
                value = 0;
                return false;
            }

            return true;
        }

        public bool ConsumeSpacePaddedNumber(int width, out int value)
        {
            int start = TextIndex;
            int spaces = 0;
            while (spaces < width - 1 && TextIndex < Text.Length && Text[TextIndex] == ' ')
            {
                spaces++;
                TextIndex++;
            }

            if (!ConsumeNumber(1, width, out value))
            {
                TextIndex = start;
                return false;
            }

            return true;
        }

        public bool ConsumeMonthName(string[] names)
        {
            bool consumed = ConsumeName(names, out Month);
            MonthSpecified |= consumed;
            return consumed;
        }

        public bool ConsumeMonthNumber(int minimumDigits, int maximumDigits)
        {
            bool consumed = ConsumeNumber(minimumDigits, maximumDigits, out Month);
            MonthSpecified |= consumed;
            return consumed;
        }

        public bool ConsumeDayNumber(int minimumDigits, int maximumDigits)
        {
            bool consumed = ConsumeNumber(minimumDigits, maximumDigits, out Day);
            DaySpecified |= consumed;
            return consumed;
        }

        public bool ConsumePaddedDay(int width)
        {
            bool consumed = ConsumeSpacePaddedNumber(width, out Day);
            DaySpecified |= consumed;
            return consumed;
        }

        public bool ConsumeTwoDigitYear()
        {
            if (!ConsumeNumber(2, 2, out int year))
            {
                return false;
            }

            Year = year >= 69 ? 1900 + year : 2000 + year;
            return true;
        }

        public bool ConsumeTwelveHour(int minimumDigits, int maximumDigits)
        {
            UsesTwelveHour = true;
            return ConsumeNumber(minimumDigits, maximumDigits, out Hour);
        }

        public bool ConsumeMeridiem(bool lowercase)
        {
            if (TextIndex + 2 > Text.Length)
            {
                return false;
            }

            ReadOnlySpan<char> value = Text.AsSpan(TextIndex, 2);
            ReadOnlySpan<char> am = lowercase ? "am" : "AM";
            ReadOnlySpan<char> pm = lowercase ? "pm" : "PM";
            if (value.SequenceEqual(am))
            {
                Meridiem = false;
            }
            else if (value.SequenceEqual(pm))
            {
                Meridiem = true;
            }
            else
            {
                return false;
            }

            TextIndex += 2;
            return true;
        }

        public bool ConsumeName(string[] names, out int ordinal)
        {
            for (int index = 0; index < names.Length; index++)
            {
                string name = names[index];
                if (name.Length > 0 &&
                    Text.AsSpan(TextIndex).StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    TextIndex += name.Length;
                    ordinal = index + 1;
                    return true;
                }
            }

            ordinal = 0;
            return false;
        }

        public bool ConsumeFraction(char separator, int maximumDigits, bool exact)
        {
            int start = TextIndex;
            if (start >= Text.Length || Text[start] != separator)
            {
                return !exact;
            }

            TextIndex++;
            int digitStart = TextIndex;
            int consumedLimit = exact ? maximumDigits : int.MaxValue;
            while (TextIndex < Text.Length &&
                TextIndex - digitStart < consumedLimit &&
                char.IsAsciiDigit(Text[TextIndex]))
            {
                TextIndex++;
            }

            int count = TextIndex - digitStart;
            if (count == 0 || (exact && count != maximumDigits))
            {
                TextIndex = start;
                return false;
            }

            int storedDigits = Math.Min(count, 9);
            int fraction = 0;
            for (int index = digitStart; index < digitStart + storedDigits; index++)
            {
                fraction = (fraction * 10) + (Text[index] - '0');
            }

            for (int index = storedDigits; index < 9; index++)
            {
                fraction *= 10;
            }

            FractionNanoseconds = fraction;
            HasFraction = true;
            return true;
        }

        public bool ConsumeNumericZone(bool allowsZulu, bool hasMinutes, bool hasSeconds, bool colon)
        {
            if (allowsZulu && TextIndex < Text.Length && Text[TextIndex] == 'Z')
            {
                TextIndex++;
                ZoneOffset = TimeSpan.Zero;
                return true;
            }

            if (TextIndex >= Text.Length || Text[TextIndex] is not ('+' or '-'))
            {
                return false;
            }

            int sign = Text[TextIndex++] == '-' ? -1 : 1;
            if (!ConsumeNumber(2, 2, out int hours))
            {
                return false;
            }

            int minutes = 0;
            int seconds = 0;
            if (hasMinutes && (!ConsumeZoneSeparator(colon) || !ConsumeNumber(2, 2, out minutes)))
            {
                return false;
            }

            if (hasSeconds && (!ConsumeZoneSeparator(colon) || !ConsumeNumber(2, 2, out seconds)))
            {
                return false;
            }

            if (hours > 24 || minutes > 60 || seconds > 60)
            {
                return false;
            }

            ZoneOffset = TimeSpan.FromSeconds(sign * ((hours * 3600L) + (minutes * 60L) + seconds));
            return true;
        }

        public bool ConsumeZoneName()
        {
            int start = TextIndex;
            if (Text.AsSpan(TextIndex).StartsWith("ChST", StringComparison.Ordinal) ||
                Text.AsSpan(TextIndex).StartsWith("MeST", StringComparison.Ordinal) ||
                Text.AsSpan(TextIndex).StartsWith("WITA", StringComparison.Ordinal))
            {
                TextIndex += 4;
            }
            else if (Text.AsSpan(TextIndex).StartsWith("GMT", StringComparison.Ordinal))
            {
                TextIndex += 3;
                int signIndex = TextIndex;
                if (TextIndex < Text.Length && Text[TextIndex] is '+' or '-')
                {
                    TextIndex++;
                    int digitStart = TextIndex;
                    while (TextIndex < Text.Length && char.IsAsciiDigit(Text[TextIndex]))
                    {
                        TextIndex++;
                    }

                    if (digitStart == TextIndex ||
                        !int.TryParse(Text.AsSpan(digitStart, TextIndex - digitStart), NumberStyles.None,
                            CultureInfo.InvariantCulture, out int hours) ||
                        hours is 0 or > 23)
                    {
                        TextIndex = signIndex;
                    }
                }
            }
            else if (TextIndex < Text.Length && Text[TextIndex] is '+' or '-')
            {
                TextIndex++;
                int digitStart = TextIndex;
                while (TextIndex < Text.Length && char.IsAsciiDigit(Text[TextIndex]))
                {
                    TextIndex++;
                }

                if (digitStart == TextIndex ||
                    !int.TryParse(Text.AsSpan(digitStart, TextIndex - digitStart), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int hours) ||
                    hours is 0 or > 23)
                {
                    TextIndex = start;
                    return false;
                }
            }
            else
            {
                while (TextIndex < Text.Length &&
                    char.IsAsciiLetterUpper(Text[TextIndex]) &&
                    TextIndex - start < 6)
                {
                    TextIndex++;
                }

                int upperLength = TextIndex - start;
                bool valid = upperLength == 3 ||
                    upperLength == 4 && Text[TextIndex - 1] == 'T' ||
                    upperLength == 5 && Text[TextIndex - 1] == 'T';
                if (!valid)
                {
                    TextIndex = start;
                    return false;
                }
            }

            int length = TextIndex - start;
            ZoneName = Text[start..TextIndex];
            return length >= 3;
        }

        public bool ApplyOrdinalDay()
        {
            if (OrdinalDay == 0)
            {
                return true;
            }

            int maximum = IsGoLeapYear(Year) ? 366 : 365;
            if (OrdinalDay > maximum)
            {
                return false;
            }

            DateTime date = new(Math.Max(1, Year), 1, 1);
            date = date.AddDays(OrdinalDay - 1);
            if (MonthSpecified && Month != date.Month || DaySpecified && Day != date.Day)
            {
                return false;
            }

            Month = date.Month;
            Day = date.Day;
            return true;
        }

        private static bool IsGoLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

        private bool ConsumeZoneSeparator(bool required)
        {
            if (!required)
            {
                return true;
            }

            if (TextIndex >= Text.Length || Text[TextIndex] != ':')
            {
                return false;
            }

            TextIndex++;
            return true;
        }
    }
}
