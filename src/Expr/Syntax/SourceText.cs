using System;
using System.Globalization;
using System.Text;

namespace Expr.Syntax;

/// <summary>Identifies a half-open range of Unicode scalar values in source text.</summary>
/// <param name="Start">The zero-based scalar offset at which the range starts.</param>
/// <param name="End">The zero-based scalar offset immediately after the range.</param>
public readonly record struct SourceLocation(int Start, int End)
{
    /// <summary>Gets the length of the range in Unicode scalar values.</summary>
    public int Length => End - Start;
}

/// <summary>Provides source text and line-oriented lookup using Expr-compatible Unicode positions.</summary>
public sealed class SourceText
{
    /// <summary>Initializes a source text instance.</summary>
    /// <param name="text">The expression source.</param>
    public SourceText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>Gets the original source text.</summary>
    public string Text { get; }

    /// <summary>Returns the requested one-based source line when it exists.</summary>
    /// <param name="line">The one-based line number.</param>
    /// <param name="snippet">The line contents, without its newline.</param>
    /// <returns><see langword="true"/> when the line exists.</returns>
    public bool TryGetLine(int line, out string snippet)
    {
        if (line < 1 || Text.Length == 0)
        {
            snippet = string.Empty;
            return false;
        }

        var start = 0;
        for (var current = 1; current < line; current++)
        {
            var newline = Text.IndexOf('\n', start);
            if (newline < 0)
            {
                snippet = string.Empty;
                return false;
            }

            start = newline + 1;
        }

        var end = Text.IndexOf('\n', start);
        snippet = end < 0 ? Text[start..] : Text[start..end];
        return true;
    }

    internal BoundSourceLocation Bind(SourceLocation location)
    {
        var line = 1;
        var column = 0;
        var scalar = 0;
        foreach (var rune in Text.EnumerateRunes())
        {
            if (scalar == location.Start)
            {
                break;
            }

            if (rune.Value == '\n')
            {
                line++;
                column = 0;
            }
            else
            {
                column++;
            }

            scalar++;
        }

        _ = TryGetLine(line, out var sourceLine);
        return new BoundSourceLocation(location, line, column, sourceLine.Replace('\t', ' '));
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>Describes a source range bound to a line and column.</summary>
/// <param name="Location">The scalar source range.</param>
/// <param name="Line">The one-based line number.</param>
/// <param name="Column">The zero-based column number.</param>
/// <param name="SourceLine">The containing source line.</param>
public readonly record struct BoundSourceLocation(
    SourceLocation Location,
    int Line,
    int Column,
    string SourceLine)
{
    /// <summary>Formats a diagnostic snippet with a caret at this location.</summary>
    /// <returns>The formatted snippet, or an empty string for an empty line.</returns>
    public string FormatSnippet()
    {
        if (SourceLine.Length == 0)
        {
            return string.Empty;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"\n | {SourceLine}\n | {new string('.', Column)}^");
    }
}
