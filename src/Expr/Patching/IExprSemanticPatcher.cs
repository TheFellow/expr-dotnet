using Expr.Checking;
using Expr.Configuration;
using Expr.Syntax;

namespace Expr.Patching;

/// <summary>Rewrites an immutable syntax tree using annotations from a completed checker pass.</summary>
public interface IExprSemanticPatcher
{
    /// <summary>Rewrites a syntax root.</summary>
    /// <param name="root">The current root.</param>
    /// <param name="model">The semantic model for the current root.</param>
    /// <param name="configuration">The active configuration.</param>
    /// <returns>The original root when unchanged, otherwise a replacement root.</returns>
    SyntaxNode Apply(SyntaxNode root, ExprSemanticModel model, ExprConfiguration configuration);
}
