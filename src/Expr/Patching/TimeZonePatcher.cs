using System;
using Expr.Checking;
using Expr.Configuration;
using Expr.Syntax;

namespace Expr.Patching;

/// <summary>Prepends a configured time zone to <c>date</c> and <c>now</c> built-ins.</summary>
public sealed class TimeZonePatcher : IExprSemanticPatcher
{
    /// <summary>Initializes a time-zone patcher.</summary>
    /// <param name="timeZone">The default time zone.</param>
    public TimeZonePatcher(TimeZoneInfo timeZone) =>
        TimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));

    /// <summary>Gets the default time zone.</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <inheritdoc />
    public SyntaxNode Apply(SyntaxNode root, ExprSemanticModel model, ExprConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(configuration);
        return new Rewriter(TimeZone).Visit(root);
    }

    private sealed class Rewriter(TimeZoneInfo timeZone) : SyntaxRewriter
    {
        protected override SyntaxNode VisitNode(SyntaxNode node)
        {
            if (node is not BuiltinNode builtin ||
                builtin.Name is not ("date" or "now") ||
                builtin.Arguments.Count > 0 && builtin.Arguments[0] is ConstantNode { Value: TimeZoneInfo })
            {
                return node;
            }

            SyntaxNode[] arguments =
                [new ConstantNode(timeZone, builtin.Location), .. builtin.Arguments];
            return Patch(
                builtin,
                new BuiltinNode(
                    builtin.Name,
                    arguments,
                    builtin.Location,
                    builtin.Throws,
                    builtin.Map,
                    builtin.Threshold));
        }
    }
}
