using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Expr.Runtime;

namespace Expr.Builtins;

internal static class ExprBuiltinStrings
{
    public static ExprInvocationResult TrimSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateTrim(arguments), Trim);

    public static ExprInvocationResult TrimPrefixSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateInput(arguments, "trimPrefix"), TrimPrefix);

    public static ExprInvocationResult TrimSuffixSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateInput(arguments, "trimSuffix"), TrimSuffix);

    public static ExprInvocationResult UpperSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateCasing(arguments, "upper"), Upper);

    public static ExprInvocationResult LowerSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateCasing(arguments, "lower"), Lower);

    public static ExprInvocationResult SplitSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateSplit(arguments, "split"), Split);

    public static ExprInvocationResult SplitAfterSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateSplit(arguments, "splitAfter"), SplitAfter);

    public static ExprInvocationResult ReplaceSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateReplace(arguments), Replace);

    public static ExprInvocationResult JoinSafe(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options) =>
        InvokeBounded(arguments, options, EstimateJoin(arguments), Join);

    public static ulong EstimateTrim(ReadOnlySpan<object?> arguments) => EstimateInput(arguments, "trim");

    public static ulong EstimateInputCost(ReadOnlySpan<object?> arguments, string name) =>
        EstimateInput(arguments, name);

    public static ulong EstimateCasing(ReadOnlySpan<object?> arguments, string name)
    {
        ulong input = EstimateInput(arguments, name);
        return CheckedCost(() => checked((input * 3UL) + 4UL));
    }

    public static ulong EstimateSplit(ReadOnlySpan<object?> arguments, string name)
    {
        string text = RequireString(arguments[0], name);
        ulong input = Utf8Cost(text);
        return CheckedCost(() => checked(input + (ulong)text.Length + 1UL));
    }

    public static ulong EstimateReplace(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "replace");
        string oldValue = RequireString(arguments[1], "replace");
        string newValue = RequireString(arguments[2], "replace");
        int requested = arguments.Length == 4 ? ToCount(arguments[3], "replace") : -1;
        if (requested == 0)
        {
            return 0;
        }

        long replacements = oldValue.Length == 0
            ? CountEmptyReplacements(text, requested)
            : CountReplacements(text, oldValue, requested);
        ulong inputCost = Utf8Cost(text);
        ulong oldCost = Utf8Cost(oldValue);
        ulong newCost = Utf8Cost(newValue);
        return CheckedCost(() => checked(
            inputCost - (oldCost * (ulong)replacements) + (newCost * (ulong)replacements)));
    }

    public static ulong EstimateJoin(ReadOnlySpan<object?> arguments)
    {
        if (!ExprCollections.TryAsArray(arguments[0], out IExprArray? array) || array is null)
        {
            throw new ExprRuntimeException(
                $"invalid argument for join (type {ExprBuiltinValues.TypeNameOf(arguments[0])})");
        }

        string separator = arguments.Length == 2 ? RequireString(arguments[1], "join") : string.Empty;
        ulong cost = array.Count > 0
            ? CheckedCost(() => checked(Utf8Cost(separator) * (ulong)(array.Count - 1)))
            : 0;
        for (int index = 0; index < array.Count; index++)
        {
            string value = RequireString(array[index], "join");
            cost = CheckedCost(() => checked(cost + Utf8Cost(value) + 1UL));
        }

        return cost;
    }

    public static object Trim(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "trim");
        return arguments.Length == 1
            ? text.Trim()
            : TrimRunes(text, RequireString(arguments[1], "trim"));
    }

    public static object TrimPrefix(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "trimPrefix");
        string prefix = arguments.Length == 1 ? " " : RequireString(arguments[1], "trimPrefix");
        return text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
    }

    public static object TrimSuffix(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "trimSuffix");
        string suffix = arguments.Length == 1 ? " " : RequireString(arguments[1], "trimSuffix");
        return text.EndsWith(suffix, StringComparison.Ordinal) ? text[..^suffix.Length] : text;
    }

    public static object Upper(ReadOnlySpan<object?> arguments) =>
        RequireString(arguments[0], "upper").ToUpperInvariant();

    public static object Lower(ReadOnlySpan<object?> arguments)
    {
        // Expr exposes lowercase conversion explicitly; uppercase normalization would change its contract.
#pragma warning disable CA1308
        return RequireString(arguments[0], "lower").ToLowerInvariant();
#pragma warning restore CA1308
    }

    public static object Split(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "split");
        string separator = RequireString(arguments[1], "split");
        int count = arguments.Length == 3 ? ToCount(arguments[2], "split") : -1;
        return SplitCore(text, separator, count, includeSeparator: false);
    }

    public static object SplitAfter(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "splitAfter");
        string separator = RequireString(arguments[1], "splitAfter");
        int count = arguments.Length == 3 ? ToCount(arguments[2], "splitAfter") : -1;
        return SplitCore(text, separator, count, includeSeparator: true);
    }

    public static object Replace(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "replace");
        string oldValue = RequireString(arguments[1], "replace");
        string newValue = RequireString(arguments[2], "replace");
        int count = arguments.Length == 4 ? ToCount(arguments[3], "replace") : -1;
        if (count == 0)
        {
            return text;
        }

        if (oldValue.Length == 0)
        {
            return ReplaceEmpty(text, newValue, count);
        }

        var result = new StringBuilder(text.Length);
        int start = 0;
        int replacements = 0;
        while ((count < 0 || replacements < count) && start <= text.Length)
        {
            int found = text.IndexOf(oldValue, start, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            result.Append(text, start, found - start);
            result.Append(newValue);
            start = found + oldValue.Length;
            replacements++;
        }

        result.Append(text, start, text.Length - start);
        return result.ToString();
    }

    public static ExprInvocationResult Repeat(ReadOnlySpan<object?> arguments, ExprBuiltinOptions options)
    {
        string text = RequireString(arguments[0], "repeat");
        long requested = ExprBuiltinValues.RequireInteger(arguments[1], "repeat");
        if (requested < 0)
        {
            throw new ExprRuntimeException(
                $"invalid argument for repeat (expected positive integer, got {requested})");
        }

        long byteCost;
        try
        {
            byteCost = checked((long)Encoding.UTF8.GetByteCount(text) * requested);
        }
        catch (OverflowException exception)
        {
            throw new ExprRuntimeException("memory budget exceeded", exception);
        }

        if (requested > options.MaximumAllocation || byteCost > options.MaximumAllocation || requested > int.MaxValue)
        {
            throw new ExprRuntimeException("memory budget exceeded");
        }

        return new ExprInvocationResult(string.Concat(Enumerable.Repeat(text, (int)requested)), (ulong)byteCost);
    }

    public static object Join(ReadOnlySpan<object?> arguments)
    {
        if (!ExprCollections.TryAsArray(arguments[0], out IExprArray? array) || array is null)
        {
            throw new ExprRuntimeException(
                $"invalid argument for join (type {ExprBuiltinValues.TypeNameOf(arguments[0])})");
        }

        string separator = arguments.Length == 2 ? RequireString(arguments[1], "join") : string.Empty;
        var values = new string[array.Count];
        for (int index = 0; index < array.Count; index++)
        {
            values[index] = RequireString(array[index], "join");
        }

        return string.Join(separator, values);
    }

    public static object IndexOf(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "indexOf");
        string value = RequireString(arguments[1], "indexOf");
        int index = text.IndexOf(value, StringComparison.Ordinal);
        return index < 0 ? -1L : Encoding.UTF8.GetByteCount(text.AsSpan(0, index));
    }

    public static object LastIndexOf(ReadOnlySpan<object?> arguments)
    {
        string text = RequireString(arguments[0], "lastIndexOf");
        string value = RequireString(arguments[1], "lastIndexOf");
        int index = text.LastIndexOf(value, StringComparison.Ordinal);
        return index < 0 ? -1L : Encoding.UTF8.GetByteCount(text.AsSpan(0, index));
    }

    public static object HasPrefix(ReadOnlySpan<object?> arguments) =>
        RequireString(arguments[0], "hasPrefix")
            .StartsWith(RequireString(arguments[1], "hasPrefix"), StringComparison.Ordinal);

    public static object HasSuffix(ReadOnlySpan<object?> arguments) =>
        RequireString(arguments[0], "hasSuffix")
            .EndsWith(RequireString(arguments[1], "hasSuffix"), StringComparison.Ordinal);

    internal static string RequireString(object? value, string name) => value as string ??
        throw new ExprRuntimeException(
            $"cannot use {ExprBuiltinValues.TypeNameOf(value)} as argument (type string) to call {name}");

    private static int ToCount(object? value, string name)
    {
        long count = ExprBuiltinValues.RequireInteger(value, name);
        return count switch
        {
            < int.MinValue => int.MinValue,
            > int.MaxValue => int.MaxValue,
            _ => (int)count,
        };
    }

    private static ExprInvocationResult InvokeBounded(
        ReadOnlySpan<object?> arguments,
        ExprBuiltinOptions options,
        ulong cost,
        ExprFunctionInvoker invoker)
    {
        if (cost > int.MaxValue || cost > (ulong)options.MaximumAllocation)
        {
            throw new ExprRuntimeException("memory budget exceeded");
        }

        return new ExprInvocationResult(invoker(arguments), cost);
    }

    private static ulong EstimateInput(ReadOnlySpan<object?> arguments, string name) =>
        Utf8Cost(RequireString(arguments[0], name));

    private static ulong Utf8Cost(string value) => CheckedCost(() => (ulong)Encoding.UTF8.GetByteCount(value));

    private static ulong CheckedCost(Func<ulong> calculate)
    {
        try
        {
            return calculate();
        }
        catch (OverflowException exception)
        {
            throw new ExprRuntimeException("memory budget exceeded", exception);
        }
    }

    private static long CountEmptyReplacements(string text, int requested)
    {
        long possible = 1;
        foreach (Rune _ in text.EnumerateRunes())
        {
            possible++;
        }

        return requested < 0 ? possible : Math.Min(requested, possible);
    }

    private static long CountReplacements(string text, string oldValue, int requested)
    {
        long replacements = 0;
        int start = 0;
        while ((requested < 0 || replacements < requested) && start <= text.Length)
        {
            int found = text.IndexOf(oldValue, start, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            replacements++;
            start = found + oldValue.Length;
        }

        return replacements;
    }

    private static string[] SplitCore(string text, string separator, int count, bool includeSeparator)
    {
        if (count == 0)
        {
            return [];
        }

        if (count == 1)
        {
            return [text];
        }

        if (separator.Length == 0)
        {
            string[] runes = text.EnumerateRunes().Select(static rune => rune.ToString()).ToArray();
            if (count < 0 || runes.Length <= count)
            {
                return runes;
            }

            string[] limited = new string[count];
            Array.Copy(runes, limited, count - 1);
            limited[^1] = string.Concat(runes.AsSpan(count - 1));
            return limited;
        }

        var values = new List<string>();
        int start = 0;
        while (count < 0 || values.Count < count - 1)
        {
            int found = text.IndexOf(separator, start, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            int end = includeSeparator ? found + separator.Length : found;
            values.Add(text[start..end]);
            start = found + separator.Length;
        }

        values.Add(text[start..]);
        return values.ToArray();
    }

    private static string ReplaceEmpty(string text, string newValue, int count)
    {
        string[] runes = text.EnumerateRunes().Select(static rune => rune.ToString()).ToArray();
        int possible = runes.Length + 1;
        int replacements = count < 0 ? possible : Math.Min(count, possible);
        var result = new StringBuilder(text.Length + (newValue.Length * replacements));
        for (int index = 0; index <= runes.Length; index++)
        {
            if (index < replacements)
            {
                result.Append(newValue);
            }

            if (index < runes.Length)
            {
                result.Append(runes[index]);
            }
        }

        return result.ToString();
    }

    private static string TrimRunes(string text, string cutset)
    {
        var removed = new HashSet<Rune>(cutset.EnumerateRunes());
        Rune[] runes = text.EnumerateRunes().ToArray();
        int first = 0;
        while (first < runes.Length && removed.Contains(runes[first]))
        {
            first++;
        }

        int last = runes.Length - 1;
        while (last >= first && removed.Contains(runes[last]))
        {
            last--;
        }

        var result = new StringBuilder();
        for (int index = first; index <= last; index++)
        {
            result.Append(runes[index]);
        }

        return result.ToString();
    }
}
