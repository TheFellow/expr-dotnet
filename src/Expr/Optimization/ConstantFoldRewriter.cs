using System;
using System.Collections.ObjectModel;
using System.Linq;
using Expr.Syntax;

namespace Expr.Optimization;

internal sealed class ConstantFoldRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node) => node switch
    {
        UnaryNode unary => FoldUnary(unary),
        BinaryNode binary => FoldBinary(binary),
        ArrayNode array => FoldArray(array),
        BuiltinNode { Name: "filter", Arguments.Count: 2 } filter => FoldFilter(filter),
        _ => node,
    };

    private SyntaxNode FoldUnary(UnaryNode node) => (node.Operator, node.Operand) switch
    {
        ("-", IntegerNode integer) => Replace(node, new IntegerNode(unchecked(-integer.Value), node.Location)),
        ("-", FloatNode number) => Replace(node, new FloatNode(-number.Value, node.Location)),
        ("+", IntegerNode integer) => Replace(node, integer),
        ("+", FloatNode number) => Replace(node, number),
        ("!" or "not", BooleanNode boolean) => Replace(node, new BooleanNode(!boolean.Value, node.Location)),
        _ => node,
    };

    private SyntaxNode FoldBinary(BinaryNode node)
    {
        if (TryNumeric(node, out SyntaxNode numeric))
        {
            return Replace(node, numeric);
        }

        return (node.Operator, node.Left, node.Right) switch
        {
            ("+", StringNode left, StringNode right) =>
                Replace(node, new StringNode(left.Value + right.Value, node.Location)),
            ("%", IntegerNode, IntegerNode { Value: 0 }) =>
                throw new ExprOptimizationException("integer divide by zero", node.Location),
            ("%", IntegerNode { Value: long.MinValue }, IntegerNode { Value: -1 }) =>
                Replace(node, new IntegerNode(0, node.Location)),
            ("%", IntegerNode left, IntegerNode right) =>
                Replace(node, new IntegerNode(unchecked(left.Value % right.Value), node.Location)),
            ("and" or "&&", BooleanNode { Value: true }, _) => Replace(node, node.Right),
            ("and" or "&&", _, BooleanNode { Value: true }) => Replace(node, node.Left),
            ("and" or "&&", BooleanNode { Value: false }, _) or
                ("and" or "&&", _, BooleanNode { Value: false }) =>
                Replace(node, new BooleanNode(false, node.Location)),
            ("or" or "||", BooleanNode { Value: false }, _) => Replace(node, node.Right),
            ("or" or "||", _, BooleanNode { Value: false }) => Replace(node, node.Left),
            ("or" or "||", BooleanNode { Value: true }, _) or
                ("or" or "||", _, BooleanNode { Value: true }) =>
                Replace(node, new BooleanNode(true, node.Location)),
            ("==", IntegerNode left, IntegerNode right) =>
                Replace(node, new BooleanNode(left.Value == right.Value, node.Location)),
            ("==", StringNode left, StringNode right) =>
                Replace(node, new BooleanNode(string.Equals(left.Value, right.Value, StringComparison.Ordinal), node.Location)),
            ("==", BooleanNode left, BooleanNode right) =>
                Replace(node, new BooleanNode(left.Value == right.Value, node.Location)),
            _ => node,
        };
    }

    private static bool IsNumeric(SyntaxNode node) => node is IntegerNode or FloatNode;

    private static bool TryNumeric(BinaryNode node, out SyntaxNode replacement)
    {
        replacement = null!;
        if (!IsNumeric(node.Left) || !IsNumeric(node.Right))
        {
            return false;
        }

        bool bothIntegers = node.Left is IntegerNode && node.Right is IntegerNode;
        long leftInteger = node.Left is IntegerNode li ? li.Value : 0;
        long rightInteger = node.Right is IntegerNode ri ? ri.Value : 0;
        double left = node.Left is IntegerNode leftInt ? leftInt.Value : ((FloatNode)node.Left).Value;
        double right = node.Right is IntegerNode rightInt ? rightInt.Value : ((FloatNode)node.Right).Value;

        SyntaxNode? candidate = node.Operator switch
        {
            "+" when bothIntegers => new IntegerNode(unchecked(leftInteger + rightInteger), node.Location),
            "-" when bothIntegers => new IntegerNode(unchecked(leftInteger - rightInteger), node.Location),
            "*" when bothIntegers => new IntegerNode(unchecked(leftInteger * rightInteger), node.Location),
            "+" => new FloatNode(left + right, node.Location),
            "-" => new FloatNode(left - right, node.Location),
            "*" => new FloatNode(left * right, node.Location),
            "/" => new FloatNode(left / right, node.Location),
            "**" or "^" => new FloatNode(Math.Pow(left, right), node.Location),
            _ => null,
        };
        if (candidate is null)
        {
            return false;
        }

        replacement = candidate;
        return true;
    }

    private SyntaxNode FoldArray(ArrayNode node)
    {
        if (node.Elements.Count is 0 || node.Elements.Any(static element => !IsScalarLiteral(element)))
        {
            return node;
        }

        object?[] values = node.Elements.Select(static element => element switch
        {
            IntegerNode integer => (object)integer.Value,
            FloatNode number => number.Value,
            StringNode text => text.Value,
            BooleanNode boolean => boolean.Value,
            _ => throw new InvalidOperationException("The scalar literal set is exhaustive."),
        }).ToArray();
        return Replace(node, new ConstantNode(new ReadOnlyCollection<object?>(values), node.Location));
    }

    private SyntaxNode FoldFilter(BuiltinNode node)
    {
        if (node.Arguments[0] is not BuiltinNode { Name: "filter", Arguments.Count: 2 } inner ||
            inner.Arguments[1] is not PredicateNode innerPredicate ||
            node.Arguments[1] is not PredicateNode outerPredicate)
        {
            return node;
        }

        var body = new BinaryNode(
            "&&",
            innerPredicate.Body,
            outerPredicate.Body,
            outerPredicate.Location);
        var predicate = new PredicateNode(body, outerPredicate.Location);
        return Replace(
            node,
            new BuiltinNode("filter", [inner.Arguments[0], predicate], node.Location));
    }

    private static bool IsScalarLiteral(SyntaxNode node) =>
        node is IntegerNode or FloatNode or StringNode or BooleanNode;
}
