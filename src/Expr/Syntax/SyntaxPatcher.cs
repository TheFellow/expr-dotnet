using System;

namespace Expr.Syntax;

/// <summary>Provides non-mutating node replacement helpers.</summary>
public static class SyntaxPatcher
{
    /// <summary>Replaces a node while retaining the original source location.</summary>
    /// <param name="original">The node being replaced.</param>
    /// <param name="replacement">The replacement node.</param>
    /// <returns>A copy of the replacement carrying the original location.</returns>
    public static SyntaxNode Replace(SyntaxNode original, SyntaxNode replacement)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        return replacement with { Location = original.Location };
    }
}
