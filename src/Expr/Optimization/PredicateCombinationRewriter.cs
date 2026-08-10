using System;
using System.Collections.Generic;
using Expr.Runtime;
using Expr.Syntax;

namespace Expr.Optimization;

internal sealed class PredicateCombinationRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BinaryNode binary ||
            !TryCombinedOperator(binary, out string? combinedOperator) ||
            binary.Left is not BuiltinNode left ||
            binary.Right is not BuiltinNode right ||
            !string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
            left.Arguments.Count < 2 ||
            right.Arguments.Count < 2 ||
            !SyntaxStructuralEquality.Equals(left.Arguments[0], right.Arguments[0]) ||
            left.Arguments[1] is not PredicateNode leftPredicate ||
            right.Arguments[1] is not PredicateNode rightPredicate)
        {
            return node;
        }

        SyntaxNode body = Visit(new BinaryNode(
            combinedOperator!,
            leftPredicate.Body,
            rightPredicate.Body,
            binary.Location));
        var predicate = new PredicateNode(body, leftPredicate.Location);
        return Replace(
            node,
            new BuiltinNode(left.Name, [left.Arguments[0], predicate], node.Location));
    }

    private static bool TryCombinedOperator(BinaryNode node, out string? combinedOperator)
    {
        combinedOperator = (node.Operator, node.Left) switch
        {
            ("and", BuiltinNode { Name: "all" }) => "and",
            ("&&", BuiltinNode { Name: "all" }) => "&&",
            ("or", BuiltinNode { Name: "any" }) => "or",
            ("||", BuiltinNode { Name: "any" }) => "||",
            ("and", BuiltinNode { Name: "none" }) => "or",
            ("&&", BuiltinNode { Name: "none" }) => "||",
            _ => null,
        };
        return combinedOperator is not null;
    }
}

internal static class SyntaxStructuralEquality
{
    public static bool Equals(SyntaxNode left, SyntaxNode right)
    {
        var pending = new Stack<(SyntaxNode Left, SyntaxNode Right)>();
        pending.Push((left, right));
        while (pending.TryPop(out (SyntaxNode Left, SyntaxNode Right) pair))
        {
            if (ReferenceEquals(pair.Left, pair.Right))
            {
                continue;
            }

            if (!EqualsOne(pair.Left, pair.Right, pending))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EqualsOne(
        SyntaxNode left,
        SyntaxNode right,
        Stack<(SyntaxNode Left, SyntaxNode Right)> pending)
    {
        switch (left, right)
        {
            case (NilNode, NilNode):
                return true;
            case (IdentifierNode a, IdentifierNode b):
                return string.Equals(a.Name, b.Name, StringComparison.Ordinal);
            case (IntegerNode a, IntegerNode b):
                return a.Value == b.Value;
            case (FloatNode a, FloatNode b):
                return a.Value.Equals(b.Value);
            case (BooleanNode a, BooleanNode b):
                return a.Value == b.Value;
            case (StringNode a, StringNode b):
                return string.Equals(a.Value, b.Value, StringComparison.Ordinal);
            case (BytesNode a, BytesNode b):
                return a.Value.Span.SequenceEqual(b.Value.Span);
            case (ConstantNode a, ConstantNode b):
                return ExprValue.Equal(a.Value, b.Value);
            case (UnaryNode a, UnaryNode b) when string.Equals(a.Operator, b.Operator, StringComparison.Ordinal):
                pending.Push((a.Operand, b.Operand));
                return true;
            case (BinaryNode a, BinaryNode b) when string.Equals(a.Operator, b.Operator, StringComparison.Ordinal):
                pending.Push((a.Right, b.Right));
                pending.Push((a.Left, b.Left));
                return true;
            case (ChainNode a, ChainNode b):
                pending.Push((a.Expression, b.Expression));
                return true;
            case (MemberNode a, MemberNode b) when a.Optional == b.Optional && a.IsMethod == b.IsMethod:
                pending.Push((a.Property, b.Property));
                pending.Push((a.Target, b.Target));
                return true;
            case (SliceNode a, SliceNode b):
                return AddOptional(a.From, b.From, pending) &&
                    AddOptional(a.To, b.To, pending) &&
                    Add(a.Target, b.Target, pending);
            case (CallNode a, CallNode b) when a.Arguments.Count == b.Arguments.Count:
                pending.Push((a.Callee, b.Callee));
                return AddList(a.Arguments, b.Arguments, pending);
            case (BuiltinNode a, BuiltinNode b) when
                string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
                a.Throws == b.Throws && a.Threshold == b.Threshold &&
                a.Arguments.Count == b.Arguments.Count:
                return AddOptional(a.Map, b.Map, pending) && AddList(a.Arguments, b.Arguments, pending);
            case (PredicateNode a, PredicateNode b):
                pending.Push((a.Body, b.Body));
                return true;
            case (PointerNode a, PointerNode b):
                return string.Equals(a.Name, b.Name, StringComparison.Ordinal);
            case (ConditionalNode a, ConditionalNode b) when a.IsTernary == b.IsTernary:
                pending.Push((a.WhenFalse, b.WhenFalse));
                pending.Push((a.WhenTrue, b.WhenTrue));
                pending.Push((a.Condition, b.Condition));
                return true;
            case (VariableDeclaratorNode a, VariableDeclaratorNode b) when
                string.Equals(a.Name, b.Name, StringComparison.Ordinal):
                pending.Push((a.Body, b.Body));
                pending.Push((a.Value, b.Value));
                return true;
            case (SequenceNode a, SequenceNode b) when a.Expressions.Count == b.Expressions.Count:
                return AddList(a.Expressions, b.Expressions, pending);
            case (ArrayNode a, ArrayNode b) when a.Elements.Count == b.Elements.Count:
                return AddList(a.Elements, b.Elements, pending);
            case (MapNode a, MapNode b) when a.Pairs.Count == b.Pairs.Count:
                for (var index = a.Pairs.Count - 1; index >= 0; index--)
                {
                    pending.Push((a.Pairs[index], b.Pairs[index]));
                }

                return true;
            case (PairNode a, PairNode b):
                pending.Push((a.Value, b.Value));
                pending.Push((a.Key, b.Key));
                return true;
            default:
                return false;
        }
    }

    private static bool AddOptional(
        SyntaxNode? left,
        SyntaxNode? right,
        Stack<(SyntaxNode Left, SyntaxNode Right)> pending)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        pending.Push((left, right));
        return true;
    }

    private static bool Add(
        SyntaxNode left,
        SyntaxNode right,
        Stack<(SyntaxNode Left, SyntaxNode Right)> pending)
    {
        pending.Push((left, right));
        return true;
    }

    private static bool AddList(
        IReadOnlyList<SyntaxNode> left,
        IReadOnlyList<SyntaxNode> right,
        Stack<(SyntaxNode Left, SyntaxNode Right)> pending)
    {
        for (var index = left.Count - 1; index >= 0; index--)
        {
            pending.Push((left[index], right[index]));
        }

        return true;
    }
}
