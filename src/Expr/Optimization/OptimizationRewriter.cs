using System;
using Expr.Syntax;

namespace Expr.Optimization;

internal abstract class OptimizationRewriter : SyntaxRewriter
{
    protected OptimizationRewriter(int maximumDepth)
    {
        MaximumDepth = maximumDepth;
    }

    public bool Applied { get; private set; }

    protected SyntaxNode Replace(SyntaxNode original, SyntaxNode replacement)
    {
        Applied = true;
        return Patch(original, replacement);
    }
}
