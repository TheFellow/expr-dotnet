using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Expr.Tests.Properties;

/// <summary>
/// Generates bounded Expr source with a version-stable pseudo-random sequence.
/// </summary>
/// <remarks>
/// The generator deliberately does not use <see cref="Random"/> so a failing seed
/// continues to identify the same expression if the framework algorithm changes.
/// </remarks>
internal sealed class DeterministicExpressionGenerator
{
    private static readonly string[] Atoms =
    [
        "nil",
        "true",
        "false",
        "0",
        "1",
        "-1",
        "1.25",
        "identifier",
        "snake_case42",
        "\"text\"",
        "'single quoted'",
        "\"line\\n😀\"",
        "b\"bytes\\x00\\xff\"",
        "`raw text`",
    ];

    private static readonly string[] BinaryOperators =
    [
        "+", "-", "*", "/", "%", "**", "==", "!=", "<", "<=", ">", ">=",
        "and", "or", "&&", "||", "in", "matches", "contains", "startsWith",
        "endsWith", "..", "??",
    ];

    private static readonly string[] Identifiers =
    [
        "value", "item", "alpha", "beta2", "snake_case", "$internal", "Δelta",
    ];

    private ulong state;

    internal DeterministicExpressionGenerator(ulong seed)
    {
        state = seed == 0 ? 0x9e3779b97f4a7c15UL : seed;
    }

    internal string GenerateSyntax(int maximumDepth) => GenerateSyntaxCore(maximumDepth);

    internal string GenerateScalarExpression(int maximumDepth) => Next(2) == 0
        ? GenerateInteger(maximumDepth)
        : GenerateBoolean(maximumDepth);

    internal int NextInt(int exclusiveMaximum) => Next(exclusiveMaximum);

    internal char NextCodeUnit()
    {
        int category = Next(8);
        return category switch
        {
            0 => (char)(0xd800 + Next(0x400)),
            1 => (char)(0xdc00 + Next(0x400)),
            2 => (char)Next(0x20),
            _ => (char)Next(char.MaxValue + 1),
        };
    }

    private string GenerateSyntaxCore(int depth)
    {
        if (depth <= 0)
        {
            return Pick(Atoms);
        }

        int nextDepth = depth - 1;
        return Next(14) switch
        {
            0 => Pick(Atoms),
            1 => $"(({GenerateSyntaxCore(nextDepth)}) {Pick(BinaryOperators)} ({GenerateSyntaxCore(nextDepth)}))",
            2 => $"{Pick(["!", "not ", "+", "-"])}({GenerateSyntaxCore(nextDepth)})",
            3 => GenerateArray(nextDepth),
            4 => GenerateMap(nextDepth),
            5 => $"(({GenerateSyntaxCore(nextDepth)}) ? ({GenerateSyntaxCore(nextDepth)}) : ({GenerateSyntaxCore(nextDepth)}))",
            6 => $"if ({GenerateSyntaxCore(nextDepth)}) {{ {GenerateSyntaxCore(nextDepth)} }} else {{ {GenerateSyntaxCore(nextDepth)} }}",
            7 => $"::custom({GenerateSyntaxCore(nextDepth)}, {GenerateSyntaxCore(nextDepth)})",
            8 => $"custom({GenerateSyntaxCore(nextDepth)}, {GenerateSyntaxCore(nextDepth)})",
            9 => $"({GenerateSyntaxCore(nextDepth)}){Pick([".", "?."])}{Pick(Identifiers)}",
            10 => $"({GenerateSyntaxCore(nextDepth)})[{GenerateInteger(1)}]",
            11 => $"({GenerateSyntaxCore(nextDepth)})[{OptionalInteger()}:{OptionalInteger()}]",
            12 => GeneratePredicate(nextDepth),
            _ => $"({GenerateSyntaxCore(nextDepth)}) | custom({GenerateSyntaxCore(nextDepth)})",
        };
    }

    private string GenerateArray(int depth)
    {
        int count = Next(4);
        var elements = new string[count];
        for (var index = 0; index < count; index++)
        {
            elements[index] = GenerateSyntaxCore(depth);
        }

        return $"[{string.Join(", ", elements)}]";
    }

    private string GenerateMap(int depth)
    {
        int count = Next(4);
        var pairs = new string[count];
        for (var index = 0; index < count; index++)
        {
            string key = Next(3) == 0
                ? $"\"key {Next(20).ToString(CultureInfo.InvariantCulture)}\""
                : Pick(Identifiers);
            pairs[index] = $"{key}: {GenerateSyntaxCore(depth)}";
        }

        return $"{{{string.Join(", ", pairs)}}}";
    }

    private string GeneratePredicate(int depth)
    {
        string collection = GenerateArray(Math.Min(depth, 1));
        return Next(5) switch
        {
            0 => $"map({collection}, #)",
            1 => $"filter({collection}, #index >= 0)",
            2 => $"count({collection}, # != nil)",
            3 => $"any({collection}, # == #)",
            _ => $"reduce({collection}, #acc ?? #)",
        };
    }

    private string GenerateInteger(int depth)
    {
        if (depth <= 0)
        {
            return (Next(41) - 20).ToString(CultureInfo.InvariantCulture);
        }

        int nextDepth = depth - 1;
        return Next(7) switch
        {
            0 => (Next(41) - 20).ToString(CultureInfo.InvariantCulture),
            1 => $"({GenerateInteger(nextDepth)} + {GenerateInteger(nextDepth)})",
            2 => $"({GenerateInteger(nextDepth)} - {GenerateInteger(nextDepth)})",
            3 => $"sum([{GenerateInteger(nextDepth)}, {GenerateInteger(nextDepth)}, {GenerateInteger(nextDepth)}])",
            4 => $"len([{GenerateInteger(nextDepth)}, {GenerateInteger(nextDepth)}])",
            5 => $"({GenerateBoolean(nextDepth)} ? {GenerateInteger(nextDepth)} : {GenerateInteger(nextDepth)})",
            _ => GenerateRangeSum(),
        };
    }

    private string GenerateBoolean(int depth)
    {
        if (depth <= 0)
        {
            return Next(2) == 0 ? "false" : "true";
        }

        int nextDepth = depth - 1;
        return Next(8) switch
        {
            0 => Next(2) == 0 ? "false" : "true",
            1 => $"({GenerateInteger(nextDepth)} {Pick(["==", "!=", "<", "<=", ">", ">="])} {GenerateInteger(nextDepth)})",
            2 => $"({GenerateBoolean(nextDepth)} && {GenerateBoolean(nextDepth)})",
            3 => $"({GenerateBoolean(nextDepth)} || {GenerateBoolean(nextDepth)})",
            4 => $"!({GenerateBoolean(nextDepth)})",
            5 => $"all([{GenerateInteger(0)}, {GenerateInteger(0)}], # >= -20)",
            6 => $"any([{GenerateInteger(0)}, {GenerateInteger(0)}], # == 0)",
            _ => $"{GenerateInteger(0)} in [{GenerateInteger(0)}, {GenerateInteger(0)}]",
        };
    }

    private string GenerateRangeSum()
    {
        int start = Next(6);
        int end = start + Next(6);
        int offset = Next(7) - 3;
        return $"sum({start.ToString(CultureInfo.InvariantCulture)}..{end.ToString(CultureInfo.InvariantCulture)}, # + {offset.ToString(CultureInfo.InvariantCulture)})";
    }

    private string OptionalInteger() => Next(3) == 0 ? string.Empty : GenerateInteger(1);

    private string Pick(IReadOnlyList<string> values) => values[Next(values.Count)];

    private int Next(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exclusiveMaximum, 1);
        ulong value = state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        state = value;
        return (int)((value * 0x2545f4914f6cdd1dUL) % (uint)exclusiveMaximum);
    }
}
