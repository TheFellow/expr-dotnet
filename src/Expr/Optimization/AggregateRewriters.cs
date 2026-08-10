using System;
using Expr.Syntax;

namespace Expr.Optimization;

internal sealed class SumRangeRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is BuiltinNode { Name: "sum" } sum && TryGetRange(sum, out long from, out long to) && to >= from)
        {
            long count = unchecked(to - from + 1);
            long total = ArithmeticSeries(from, to, count);
            if (sum.Arguments.Count is 1)
            {
                return Replace(node, new IntegerNode(total, node.Location));
            }

            if (sum.Arguments.Count is 2 && TryApplyProjection(total, count, sum.Arguments[1], out long result))
            {
                return Replace(node, new IntegerNode(result, node.Location));
            }
        }

        if (node is BuiltinNode { Name: "reduce" } reduce &&
            TryGetRange(reduce, out long reduceFrom, out long reduceTo) &&
            reduceTo >= reduceFrom &&
            reduce.Arguments.Count is 2 or 3 &&
            IsPointerPlusAccumulator(reduce.Arguments[1]))
        {
            long count = unchecked(reduceTo - reduceFrom + 1);
            long total = ArithmeticSeries(reduceFrom, reduceTo, count);
            if (reduce.Arguments.Count is 2)
            {
                return Replace(node, new IntegerNode(total, node.Location));
            }

            if (reduce.Arguments[2] is IntegerNode initial)
            {
                return Replace(node, new IntegerNode(unchecked(initial.Value + total), node.Location));
            }
        }

        return node;
    }

    private static bool TryGetRange(BuiltinNode node, out long from, out long to)
    {
        if (node.Arguments.Count is 1 or 2 or 3 &&
            node.Arguments[0] is BinaryNode
            {
                Operator: "..",
                Left: IntegerNode lower,
                Right: IntegerNode upper,
            })
        {
            from = lower.Value;
            to = upper.Value;
            return true;
        }

        from = 0;
        to = 0;
        return false;
    }

    private static long ArithmeticSeries(long from, long to, long count) =>
        unchecked(count * unchecked(from + to) / 2);

    private static bool IsPointerPlusAccumulator(SyntaxNode node) =>
        node is PredicateNode
        {
            Body: BinaryNode
            {
                Operator: "+",
                Left: PointerNode left,
                Right: PointerNode right,
            },
        } &&
        (left.Name.Length is 0 && string.Equals(right.Name, "acc", StringComparison.Ordinal) ||
         string.Equals(left.Name, "acc", StringComparison.Ordinal) && right.Name.Length is 0);

    private static bool TryApplyProjection(long sum, long count, SyntaxNode node, out long result)
    {
        if (node is PredicateNode { Body: PointerNode { Name.Length: 0 } })
        {
            result = sum;
            return true;
        }

        if (node is not PredicateNode { Body: BinaryNode binary } ||
            !TryPointerAndConstant(binary, out long constant, out bool pointerOnLeft))
        {
            result = 0;
            return false;
        }

        result = binary.Operator switch
        {
            "*" => unchecked(constant * sum),
            "+" => unchecked(sum + unchecked(count * constant)),
            "-" when pointerOnLeft => unchecked(sum - unchecked(count * constant)),
            "-" => unchecked(unchecked(count * constant) - sum),
            _ => 0,
        };
        return binary.Operator is "*" or "+" or "-";
    }

    private static bool TryPointerAndConstant(BinaryNode node, out long constant, out bool pointerOnLeft)
    {
        if (node.Left is PointerNode { Name.Length: 0 } && node.Right is IntegerNode right)
        {
            constant = right.Value;
            pointerOnLeft = true;
            return true;
        }

        if (node.Left is IntegerNode left && node.Right is PointerNode { Name.Length: 0 })
        {
            constant = left.Value;
            pointerOnLeft = false;
            return true;
        }

        constant = 0;
        pointerOnLeft = false;
        return false;
    }
}

internal sealed class SumArrayRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    private const int MaximumFoldedArrayLength = 256;

    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BuiltinNode
            {
                Name: "sum",
                Arguments: [ArrayNode { Elements.Count: >= 2 and <= MaximumFoldedArrayLength } array],
            })
        {
            return node;
        }

        SyntaxNode expression = array.Elements[^1];
        for (var index = array.Elements.Count - 2; index >= 0; index--)
        {
            expression = new BinaryNode("+", array.Elements[index], expression, node.Location);
        }

        return Replace(node, expression);
    }
}

internal sealed class SumMapRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BuiltinNode
            {
                Name: "sum",
                Arguments: [BuiltinNode { Name: "map", Arguments.Count: 2 } map],
            })
        {
            return node;
        }

        return Replace(node, new BuiltinNode("sum", map.Arguments, node.Location));
    }
}

internal sealed class CountAnyRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is BinaryNode
            {
                Left: BuiltinNode { Name: "count", Arguments.Count: 2 } count,
                Right: IntegerNode integer,
            } binary &&
            (binary.Operator is ">" && integer.Value is 0 ||
             binary.Operator is ">=" && integer.Value is 1))
        {
            return Replace(node, new BuiltinNode("any", count.Arguments, node.Location));
        }

        return node;
    }
}

internal sealed class CountThresholdRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BinaryNode
            {
                Left: BuiltinNode { Name: "count", Arguments.Count: 2 } count,
                Right: IntegerNode { Value: >= 0 } integer,
            } binary)
        {
            return node;
        }

        long threshold = binary.Operator switch
        {
            ">" or "<=" => unchecked(integer.Value + 1),
            ">=" or "<" => integer.Value,
            _ => 0,
        };
        if (threshold is <= 1 or > int.MaxValue)
        {
            return node;
        }

        var replacementCount = new BuiltinNode(
            count.Name,
            count.Arguments,
            count.Location,
            count.Throws,
            count.Map,
            (int)threshold);
        return Replace(node, binary with { Left = replacementCount });
    }
}
