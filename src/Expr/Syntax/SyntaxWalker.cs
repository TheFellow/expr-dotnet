using System;
using System.Collections.Generic;

namespace Expr.Syntax;

/// <summary>Receives nodes visited by <see cref="SyntaxWalker"/>.</summary>
public interface ISyntaxVisitor
{
    /// <summary>Visits one node after its children have been visited.</summary>
    /// <param name="node">The node.</param>
    void Visit(SyntaxNode node);
}

/// <summary>Walks and searches immutable Expr syntax trees without recursive stack growth.</summary>
public static class SyntaxWalker
{
    /// <summary>Walks a tree depth-first and invokes the visitor after each node's children.</summary>
    /// <param name="node">The root node.</param>
    /// <param name="visitor">The visitor.</param>
    public static void Walk(SyntaxNode node, ISyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var current in TraversePostOrder(node))
        {
            visitor.Visit(current);
        }
    }

    /// <summary>Finds the last node in post-order that satisfies a predicate.</summary>
    /// <param name="node">The root node.</param>
    /// <param name="predicate">The match predicate.</param>
    /// <returns>The last matching node, or <see langword="null"/>.</returns>
    public static SyntaxNode? Find(SyntaxNode node, Func<SyntaxNode, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(predicate);
        SyntaxNode? result = null;
        foreach (var current in TraversePostOrder(node))
        {
            if (predicate(current))
            {
                result = current;
            }
        }

        return result;
    }

    private static IEnumerable<SyntaxNode> TraversePostOrder(SyntaxNode root)
    {
        var stack = new Stack<(SyntaxNode Node, bool Visited)>();
        stack.Push((root, false));
        while (stack.Count > 0)
        {
            var (node, visited) = stack.Pop();
            if (visited)
            {
                yield return node;
                continue;
            }

            stack.Push((node, true));
            PushChildrenInReverse(stack, node);
        }
    }

    private static void PushChildrenInReverse(Stack<(SyntaxNode Node, bool Visited)> stack, SyntaxNode node)
    {
        switch (node)
        {
            case UnaryNode unary:
                Push(stack, unary.Operand);
                break;
            case BinaryNode binary:
                Push(stack, binary.Right);
                Push(stack, binary.Left);
                break;
            case ChainNode chain:
                Push(stack, chain.Expression);
                break;
            case MemberNode member:
                Push(stack, member.Property);
                Push(stack, member.Target);
                break;
            case SliceNode slice:
                if (slice.To is not null)
                {
                    Push(stack, slice.To);
                }

                if (slice.From is not null)
                {
                    Push(stack, slice.From);
                }

                Push(stack, slice.Target);
                break;
            case CallNode call:
                PushListInReverse(stack, call.Arguments);
                Push(stack, call.Callee);
                break;
            case BuiltinNode builtin:
                if (builtin.Map is not null)
                {
                    Push(stack, builtin.Map);
                }

                PushListInReverse(stack, builtin.Arguments);
                break;
            case PredicateNode predicate:
                Push(stack, predicate.Body);
                break;
            case ConditionalNode conditional:
                Push(stack, conditional.WhenFalse);
                Push(stack, conditional.WhenTrue);
                Push(stack, conditional.Condition);
                break;
            case VariableDeclaratorNode variable:
                Push(stack, variable.Body);
                Push(stack, variable.Value);
                break;
            case SequenceNode sequence:
                PushListInReverse(stack, sequence.Expressions);
                break;
            case ArrayNode array:
                PushListInReverse(stack, array.Elements);
                break;
            case MapNode map:
                PushListInReverse(stack, map.Pairs);
                break;
            case PairNode pair:
                Push(stack, pair.Value);
                Push(stack, pair.Key);
                break;
            case NilNode or IdentifierNode or IntegerNode or FloatNode or BooleanNode or
                StringNode or BytesNode or ConstantNode or PointerNode:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node.GetType(), "Unknown syntax node type.");
        }
    }

    private static void PushListInReverse<T>(
        Stack<(SyntaxNode Node, bool Visited)> stack,
        IReadOnlyList<T> nodes)
        where T : SyntaxNode
    {
        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            Push(stack, nodes[index]);
        }
    }

    private static void Push(Stack<(SyntaxNode Node, bool Visited)> stack, SyntaxNode node) =>
        stack.Push((node, false));
}

/// <summary>Rewrites immutable Expr trees and preserves unchanged node instances.</summary>
public abstract class SyntaxRewriter
{
    /// <summary>Gets or initializes the maximum tree depth accepted by this rewriter.</summary>
    public int MaximumDepth { get; init; } = 1_024;

    /// <summary>Recursively rewrites a node and its children.</summary>
    /// <param name="node">The node to rewrite.</param>
    /// <returns>The original node when unchanged, otherwise its replacement.</returns>
    public SyntaxNode Visit(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (MaximumDepth <= 0)
        {
            throw new InvalidOperationException("Syntax rewriter maximum depth must be positive.");
        }

        return VisitCore(node, 0);
    }

    /// <summary>Allows a derived rewriter to replace a node after its children are rewritten.</summary>
    /// <param name="node">The node with rewritten children.</param>
    /// <returns>The original or replacement node.</returns>
    protected virtual SyntaxNode VisitNode(SyntaxNode node) => node;

    /// <summary>Copies the original source location to a replacement node.</summary>
    /// <param name="original">The node being replaced.</param>
    /// <param name="replacement">The replacement node.</param>
    /// <returns>The replacement with the original location.</returns>
    protected static SyntaxNode Patch(SyntaxNode original, SyntaxNode replacement) =>
        SyntaxPatcher.Replace(original, replacement);

    private SyntaxNode VisitCore(SyntaxNode node, int depth)
    {
        if (depth >= MaximumDepth)
        {
            throw new InvalidOperationException(
                $"Syntax tree exceeds the configured rewriter depth limit of {MaximumDepth}.");
        }

        var rewritten = RewriteChildren(node, depth + 1);
        return VisitNode(rewritten);
    }

    private SyntaxNode RewriteChildren(SyntaxNode node, int childDepth) => node switch
    {
        UnaryNode n => RewriteUnary(n, childDepth),
        BinaryNode n => RewriteBinary(n, childDepth),
        ChainNode n => RewriteChain(n, childDepth),
        MemberNode n => RewriteMember(n, childDepth),
        SliceNode n => RewriteSlice(n, childDepth),
        CallNode n => RewriteCall(n, childDepth),
        BuiltinNode n => RewriteBuiltin(n, childDepth),
        PredicateNode n => RewritePredicate(n, childDepth),
        ConditionalNode n => RewriteConditional(n, childDepth),
        VariableDeclaratorNode n => RewriteVariable(n, childDepth),
        SequenceNode n => RewriteSequence(n, childDepth),
        ArrayNode n => RewriteArray(n, childDepth),
        MapNode n => RewriteMap(n, childDepth),
        PairNode n => RewritePair(n, childDepth),
        _ => node,
    };

    private SyntaxNode RewriteUnary(UnaryNode node, int depth)
    {
        var operand = VisitCore(node.Operand, depth);
        return ReferenceEquals(operand, node.Operand) ? node : node with { Operand = operand };
    }

    private SyntaxNode RewriteBinary(BinaryNode node, int depth)
    {
        var left = VisitCore(node.Left, depth);
        var right = VisitCore(node.Right, depth);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    private SyntaxNode RewriteChain(ChainNode node, int depth)
    {
        var expression = VisitCore(node.Expression, depth);
        return ReferenceEquals(expression, node.Expression) ? node : node with { Expression = expression };
    }

    private SyntaxNode RewriteMember(MemberNode node, int depth)
    {
        var target = VisitCore(node.Target, depth);
        var property = VisitCore(node.Property, depth);
        return ReferenceEquals(target, node.Target) && ReferenceEquals(property, node.Property)
            ? node
            : node with { Target = target, Property = property };
    }

    private SyntaxNode RewriteSlice(SliceNode node, int depth)
    {
        var target = VisitCore(node.Target, depth);
        var from = VisitOptional(node.From, depth);
        var to = VisitOptional(node.To, depth);
        return ReferenceEquals(target, node.Target) && ReferenceEquals(from, node.From) && ReferenceEquals(to, node.To)
            ? node
            : node with { Target = target, From = from, To = to };
    }

    private SyntaxNode RewriteCall(CallNode node, int depth)
    {
        var callee = VisitCore(node.Callee, depth);
        var arguments = RewriteList(node.Arguments, depth);
        return ReferenceEquals(callee, node.Callee) && ReferenceEquals(arguments, node.Arguments)
            ? node
            : new CallNode(callee, arguments, node.Location);
    }

    private SyntaxNode RewriteBuiltin(BuiltinNode node, int depth)
    {
        var arguments = RewriteList(node.Arguments, depth);
        var map = VisitOptional(node.Map, depth);
        return ReferenceEquals(arguments, node.Arguments) && ReferenceEquals(map, node.Map)
            ? node
            : new BuiltinNode(node.Name, arguments, node.Location, node.Throws, map, node.Threshold);
    }

    private SyntaxNode RewritePredicate(PredicateNode node, int depth)
    {
        var body = VisitCore(node.Body, depth);
        return ReferenceEquals(body, node.Body) ? node : node with { Body = body };
    }

    private SyntaxNode RewriteConditional(ConditionalNode node, int depth)
    {
        var condition = VisitCore(node.Condition, depth);
        var whenTrue = VisitCore(node.WhenTrue, depth);
        var whenFalse = VisitCore(node.WhenFalse, depth);
        return ReferenceEquals(condition, node.Condition) &&
            ReferenceEquals(whenTrue, node.WhenTrue) &&
            ReferenceEquals(whenFalse, node.WhenFalse)
            ? node
            : node with { Condition = condition, WhenTrue = whenTrue, WhenFalse = whenFalse };
    }

    private SyntaxNode RewriteVariable(VariableDeclaratorNode node, int depth)
    {
        var value = VisitCore(node.Value, depth);
        var body = VisitCore(node.Body, depth);
        return ReferenceEquals(value, node.Value) && ReferenceEquals(body, node.Body)
            ? node
            : node with { Value = value, Body = body };
    }

    private SyntaxNode RewriteSequence(SequenceNode node, int depth)
    {
        var expressions = RewriteList(node.Expressions, depth);
        return ReferenceEquals(expressions, node.Expressions) ? node : new SequenceNode(expressions, node.Location);
    }

    private SyntaxNode RewriteArray(ArrayNode node, int depth)
    {
        var elements = RewriteList(node.Elements, depth);
        return ReferenceEquals(elements, node.Elements) ? node : new ArrayNode(elements, node.Location);
    }

    private SyntaxNode RewriteMap(MapNode node, int depth)
    {
        var pairs = RewritePairs(node.Pairs, depth);
        return ReferenceEquals(pairs, node.Pairs) ? node : new MapNode(pairs, node.Location);
    }

    private SyntaxNode RewritePair(PairNode node, int depth)
    {
        var key = VisitCore(node.Key, depth);
        var value = VisitCore(node.Value, depth);
        return ReferenceEquals(key, node.Key) && ReferenceEquals(value, node.Value)
            ? node
            : node with { Key = key, Value = value };
    }

    private SyntaxNode? VisitOptional(SyntaxNode? node, int depth) =>
        node is null ? null : VisitCore(node, depth);

    private IReadOnlyList<SyntaxNode> RewriteList(IReadOnlyList<SyntaxNode> nodes, int depth)
    {
        SyntaxNode[]? replacements = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var replacement = VisitCore(nodes[index], depth);
            if (!ReferenceEquals(replacement, nodes[index]) && replacements is null)
            {
                replacements = new SyntaxNode[nodes.Count];
                for (var prior = 0; prior < index; prior++)
                {
                    replacements[prior] = nodes[prior];
                }
            }

            if (replacements is not null)
            {
                replacements[index] = replacement;
            }
        }

        return replacements is null ? nodes : SyntaxCollections.Copy(replacements);
    }

    private IReadOnlyList<PairNode> RewritePairs(IReadOnlyList<PairNode> nodes, int depth)
    {
        PairNode[]? replacements = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var replacement = (PairNode)VisitCore(nodes[index], depth);
            if (!ReferenceEquals(replacement, nodes[index]) && replacements is null)
            {
                replacements = new PairNode[nodes.Count];
                for (var prior = 0; prior < index; prior++)
                {
                    replacements[prior] = nodes[prior];
                }
            }

            if (replacements is not null)
            {
                replacements[index] = replacement;
            }
        }

        return replacements is null ? nodes : SyntaxCollections.Copy(replacements);
    }
}
