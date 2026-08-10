using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Expr.Checking;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Optimization;

internal sealed class InArrayRewriter(
    ExprSemanticModel semanticModel,
    int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BinaryNode
            {
                Operator: "in",
                Right: ArrayNode { Elements.Count: > 0 } array,
            } binary)
        {
            return node;
        }

        if (All<IntegerNode>(array.Elements) && IsInteger(binary.Left))
        {
            var values = new Dictionary<long, object?>();
            foreach (SyntaxNode element in array.Elements)
            {
                values[((IntegerNode)element).Value] = null;
            }

            return Replace(
                node,
                binary with
                {
                    Right = new ConstantNode(
                        new ReadOnlyDictionary<long, object?>(values),
                        array.Location),
                });
        }

        if (All<StringNode>(array.Elements))
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (SyntaxNode element in array.Elements)
            {
                values[((StringNode)element).Value] = null;
            }

            return Replace(
                node,
                binary with
                {
                    Right = new ConstantNode(
                        new ReadOnlyDictionary<string, object?>(values),
                        array.Location),
                });
        }

        return node;
    }

    private bool IsInteger(SyntaxNode node) =>
        node is IntegerNode ||
        semanticModel.TryGetSemantics(node, out ExprNodeSemantics? semantics) &&
        semantics?.Type.Kind is ExprTypeKind.Integer;

    private static bool All<T>(IReadOnlyList<SyntaxNode> nodes)
        where T : SyntaxNode
    {
        foreach (SyntaxNode node in nodes)
        {
            if (node is not T)
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class InRangeRewriter(
    ExprSemanticModel semanticModel,
    int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BinaryNode
            {
                Operator: "in",
                Right: BinaryNode
                {
                    Operator: "..",
                    Left: IntegerNode from,
                    Right: IntegerNode to,
                },
            } binary ||
            !IsInteger(binary.Left))
        {
            return node;
        }

        var lower = new BinaryNode(">=", binary.Left, from, node.Location);
        var upper = new BinaryNode("<=", binary.Left, to, node.Location);
        return Replace(node, new BinaryNode("and", lower, upper, node.Location));
    }

    private bool IsInteger(SyntaxNode node) =>
        node is IntegerNode ||
        semanticModel.TryGetSemantics(node, out ExprNodeSemantics? semantics) &&
        semantics?.Type.Kind is ExprTypeKind.Integer;
}
