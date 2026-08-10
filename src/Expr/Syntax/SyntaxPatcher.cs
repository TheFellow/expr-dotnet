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

    /// <summary>Replaces every reference to a target node in an immutable tree.</summary>
    /// <param name="root">The root syntax node.</param>
    /// <param name="target">The exact node instance to replace.</param>
    /// <param name="replacement">The replacement, which inherits the target location.</param>
    /// <returns>The original root when the target is absent, otherwise a minimally copied tree.</returns>
    public static SyntaxNode Replace(SyntaxNode root, SyntaxNode target, SyntaxNode replacement)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replacement);
        return new TargetRewriter(target, replacement).Visit(root);
    }

    private sealed class TargetRewriter(SyntaxNode target, SyntaxNode replacement) : SyntaxRewriter
    {
        protected override SyntaxNode VisitNode(SyntaxNode node) => ReferenceEquals(node, target)
            ? Replace(node, replacement)
            : node;
    }
}
