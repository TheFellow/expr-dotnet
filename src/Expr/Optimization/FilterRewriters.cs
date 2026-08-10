using Expr.Syntax;

namespace Expr.Optimization;

internal sealed class FilterMapRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BuiltinNode
            {
                Name: "map",
                Arguments.Count: 2,
                Arguments: [BuiltinNode { Name: "filter", Map: null } filter, PredicateNode projection],
            } ||
            SyntaxWalker.Find(projection, static candidate => candidate is PointerNode { Name: "index" }) is not null)
        {
            return node;
        }

        return Replace(
            node,
            new BuiltinNode(
                "filter",
                filter.Arguments,
                node.Location,
                map: projection.Body));
    }
}

internal sealed class FilterLengthRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not BuiltinNode
            {
                Name: "len",
                Arguments: [BuiltinNode { Name: "filter", Arguments.Count: 2 } filter],
            })
        {
            return node;
        }

        return Replace(node, new BuiltinNode("count", filter.Arguments, node.Location));
    }
}

internal sealed class FilterLastRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is MemberNode
            {
                Optional: false,
                Target: BuiltinNode { Name: "filter", Arguments.Count: 2 } filter,
                Property: IntegerNode { Value: -1 },
            })
        {
            return Replace(
                node,
                new BuiltinNode("findLast", filter.Arguments, node.Location, true, filter.Map));
        }

        if (node is BuiltinNode
            {
                Name: "last",
                Arguments: [BuiltinNode { Name: "filter", Arguments.Count: 2 } lastFilter],
            })
        {
            return Replace(
                node,
                new BuiltinNode("findLast", lastFilter.Arguments, node.Location, false, lastFilter.Map));
        }

        return node;
    }
}

internal sealed class FilterFirstRewriter(int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is MemberNode
            {
                Optional: false,
                Target: BuiltinNode { Name: "filter", Arguments.Count: 2 } filter,
                Property: IntegerNode { Value: 0 },
            })
        {
            return Replace(
                node,
                new BuiltinNode("find", filter.Arguments, node.Location, true, filter.Map));
        }

        if (node is BuiltinNode
            {
                Name: "first",
                Arguments: [BuiltinNode { Name: "filter", Arguments.Count: 2 } firstFilter],
            })
        {
            return Replace(
                node,
                new BuiltinNode("find", firstFilter.Arguments, node.Location, false, firstFilter.Map));
        }

        return node;
    }
}
