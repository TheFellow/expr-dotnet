using System;
using System.Globalization;
using System.Text;

namespace Expr.Syntax;

/// <summary>Identifies a half-open range of Unicode scalar values in source text.</summary>
public readonly record struct SourceLocation
{
    private readonly int start;
    private readonly int end;

    /// <summary>Initializes a valid half-open source range.</summary>
    public SourceLocation(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The end of a source range cannot precede its start.");
        }

        this.start = start;
        this.end = end;
    }

    /// <summary>Gets the zero-based scalar offset at which the range starts.</summary>
    public int Start
    {
        get => start;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value > end)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The start of a source range cannot follow its end.");
            }

            start = value;
        }
    }

    /// <summary>Gets the zero-based scalar offset immediately after the range.</summary>
    public int End
    {
        get => end;
        init
        {
            if (value < start)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The end of a source range cannot precede its start.");
            }

            end = value;
        }
    }

    /// <summary>Gets the length of the range in Unicode scalar values.</summary>
    public int Length => End - Start;

    /// <summary>Deconstructs the source range.</summary>
    public void Deconstruct(out int start, out int end) => (start, end) = (Start, End);
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
