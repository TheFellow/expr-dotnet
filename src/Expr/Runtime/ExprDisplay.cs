using System;
using System.Globalization;

namespace Expr.Runtime;

internal static class ExprDisplay
{
    internal static string Value(object? value) => value switch
    {
        null => "nil",
        string text => text,
        char character => character.ToString(CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        byte number => number.ToString(CultureInfo.InvariantCulture),
        sbyte number => number.ToString(CultureInfo.InvariantCulture),
        short number => number.ToString(CultureInfo.InvariantCulture),
        ushort number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        uint number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        ulong number => number.ToString(CultureInfo.InvariantCulture),
        nint number => number.ToString(CultureInfo.InvariantCulture),
        nuint number => number.ToString(CultureInfo.InvariantCulture),
        Half number => number.ToString(CultureInfo.InvariantCulture),
        float number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        DateTime instant => instant.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset instant => instant.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
        Guid identifier => identifier.ToString("D", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        ReadOnlyMemory<byte> bytes => Convert.ToHexString(bytes.Span),
        _ => $"<{value.GetType().FullName}>",
    };
}
