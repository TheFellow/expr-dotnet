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

/// <summary>Specifies when a node is yielded relative to its children.</summary>
public enum SyntaxTraversalOrder
{
    /// <summary>Yields each node before its children.</summary>
    PreOrder,

    /// <summary>Yields each node after its children.</summary>
    PostOrder,
}

/// <summary>Configures guarded syntax-tree traversal.</summary>
public sealed record SyntaxWalkerOptions
{
    /// <summary>Gets or initializes the maximum nested syntax depth.</summary>
    public int MaximumDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 65_536;

    /// <summary>Gets or initializes the maximum number of visited node occurrences.</summary>
    public int MaximumNodeCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_000_000;
}

/// <summary>Walks and searches immutable Expr syntax trees without recursive stack growth.</summary>
public static class SyntaxWalker
{
    /// <summary>Walks a tree depth-first and invokes the visitor after each node's children.</summary>
    /// <param name="node">The root node.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="options">Optional traversal limits.</param>
    public static void Walk(SyntaxNode node, ISyntaxVisitor visitor, SyntaxWalkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var current in Traverse(node, SyntaxTraversalOrder.PostOrder, options))
        {
            visitor.Visit(current);
        }
    }

    /// <summary>Walks a tree depth-first and invokes a delegate after each node's children.</summary>
    /// <param name="node">The root node.</param>
    /// <param name="visitor">The visitor delegate.</param>
    /// <param name="options">Optional traversal limits.</param>
    public static void Walk(SyntaxNode node, Action<SyntaxNode> visitor, SyntaxWalkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var current in Traverse(node, SyntaxTraversalOrder.PostOrder, options))
        {
            visitor(current);
        }
    }

    /// <summary>Finds the last node in post-order that satisfies a predicate.</summary>
    /// <param name="node">The root node.</param>
    /// <param name="predicate">The match predicate.</param>
    /// <returns>The last matching node, or <see langword="null"/>.</returns>
    /// <param name="options">Optional traversal limits.</param>
    public static SyntaxNode? Find(
        SyntaxNode node,
        Func<SyntaxNode, bool> predicate,
        SyntaxWalkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(predicate);
        SyntaxNode? result = null;
        foreach (var current in Traverse(node, SyntaxTraversalOrder.PostOrder, options))
        {
            if (predicate(current))
            {
                result = current;
            }
        }

        return result;
    }

    /// <summary>Enumerates a tree in deterministic depth-first order.</summary>
    /// <param name="node">The root node, which is included in the result.</param>
    /// <param name="order">Whether nodes are yielded before or after their children.</param>
    /// <param name="options">Optional traversal limits.</param>
    /// <returns>A guarded, lazy syntax-node sequence.</returns>
    public static IEnumerable<SyntaxNode> Traverse(
        SyntaxNode node,
        SyntaxTraversalOrder order = SyntaxTraversalOrder.PostOrder,
        SyntaxWalkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!Enum.IsDefined(order))
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown syntax traversal order.");
        }

        var limits = options ?? new SyntaxWalkerOptions();
        var stack = new Stack<TraversalFrame>();
        var nodeCount = 0;
        stack.Push(new TraversalFrame(node, 0, false));
        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            if (frame.Exiting)
            {
                if (order == SyntaxTraversalOrder.PostOrder)
                {
                    yield return frame.Node;
                }

                continue;
            }

            if (frame.Depth >= limits.MaximumDepth)
            {
                throw new InvalidOperationException(
                    $"Syntax tree exceeds the configured walker depth limit of {limits.MaximumDepth}.");
            }

            nodeCount++;
            if (nodeCount > limits.MaximumNodeCount)
            {
                throw new InvalidOperationException(
                    $"Syntax tree exceeds the configured walker node limit of {limits.MaximumNodeCount}.");
            }

            stack.Push(frame with { Exiting = true });
            PushChildrenInReverse(stack, frame.Node, frame.Depth + 1);

            if (order == SyntaxTraversalOrder.PreOrder)
            {
                yield return frame.Node;
            }
        }
    }

    private static void PushChildrenInReverse(Stack<TraversalFrame> stack, SyntaxNode node, int depth)
    {
        switch (node)
        {
            case UnaryNode unary:
                Push(stack, unary.Operand, depth);
                break;
            case BinaryNode binary:
                Push(stack, binary.Right, depth);
                Push(stack, binary.Left, depth);
                break;
            case ChainNode chain:
                Push(stack, chain.Expression, depth);
                break;
            case MemberNode member:
                Push(stack, member.Property, depth);
                Push(stack, member.Target, depth);
                break;
            case SliceNode slice:
                if (slice.To is not null)
                {
                    Push(stack, slice.To, depth);
                }

                if (slice.From is not null)
                {
                    Push(stack, slice.From, depth);
                }

                Push(stack, slice.Target, depth);
                break;
            case CallNode call:
                PushListInReverse(stack, call.Arguments, depth);
                Push(stack, call.Callee, depth);
                break;
            case BuiltinNode builtin:
                if (builtin.Map is not null)
                {
                    Push(stack, builtin.Map, depth);
                }

                PushListInReverse(stack, builtin.Arguments, depth);
                break;
            case PredicateNode predicate:
                Push(stack, predicate.Body, depth);
                break;
            case ConditionalNode conditional:
                Push(stack, conditional.WhenFalse, depth);
                Push(stack, conditional.WhenTrue, depth);
                Push(stack, conditional.Condition, depth);
                break;
            case VariableDeclaratorNode variable:
                Push(stack, variable.Body, depth);
                Push(stack, variable.Value, depth);
                break;
            case SequenceNode sequence:
                PushListInReverse(stack, sequence.Expressions, depth);
                break;
            case ArrayNode array:
                PushListInReverse(stack, array.Elements, depth);
                break;
            case MapNode map:
                PushListInReverse(stack, map.Pairs, depth);
                break;
            case PairNode pair:
                Push(stack, pair.Value, depth);
                Push(stack, pair.Key, depth);
                break;
            case NilNode or IdentifierNode or IntegerNode or FloatNode or BooleanNode or
                StringNode or BytesNode or ConstantNode or PointerNode:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node.GetType(), "Unknown syntax node type.");
        }
    }

    private static void PushListInReverse<T>(Stack<TraversalFrame> stack, IReadOnlyList<T> nodes, int depth)
        where T : SyntaxNode
    {
        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            Push(stack, nodes[index], depth);
        }
    }

    private static void Push(Stack<TraversalFrame> stack, SyntaxNode node, int depth) =>
        stack.Push(new TraversalFrame(node, depth, false));

    /// <summary>Gets the immediate children of a node in source evaluation order.</summary>
    /// <param name="node">The syntax node.</param>
    /// <returns>An immutable snapshot of immediate children.</returns>
    public static IReadOnlyList<SyntaxNode> GetChildren(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            UnaryNode unary => [unary.Operand],
            BinaryNode binary => [binary.Left, binary.Right],
            ChainNode chain => [chain.Expression],
            MemberNode member => [member.Target, member.Property],
            SliceNode { From: null, To: null } slice => [slice.Target],
            SliceNode { From: null } slice => [slice.Target, slice.To!],
            SliceNode { To: null } slice => [slice.Target, slice.From],
            SliceNode slice => [slice.Target, slice.From, slice.To],
            CallNode call => CopyWithHead(call.Callee, call.Arguments),
            BuiltinNode { Map: null } builtin => SyntaxCollections.Copy(builtin.Arguments),
            BuiltinNode builtin => CopyWithTail(builtin.Arguments, builtin.Map),
            PredicateNode predicate => [predicate.Body],
            ConditionalNode conditional => [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            VariableDeclaratorNode variable => [variable.Value, variable.Body],
            SequenceNode sequence => SyntaxCollections.Copy(sequence.Expressions),
            ArrayNode array => SyntaxCollections.Copy(array.Elements),
            MapNode map => SyntaxCollections.Copy<SyntaxNode>(map.Pairs),
            PairNode pair => [pair.Key, pair.Value],
            NilNode or IdentifierNode or IntegerNode or FloatNode or BooleanNode or
                StringNode or BytesNode or ConstantNode or PointerNode => Array.Empty<SyntaxNode>(),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType(), "Unknown syntax node type."),
        };
    }

    private static IReadOnlyList<SyntaxNode> CopyWithHead(
        SyntaxNode head,
        IReadOnlyList<SyntaxNode> values)
    {
        var result = new SyntaxNode[values.Count + 1];
        result[0] = head;
        for (var index = 0; index < values.Count; index++)
        {
            result[index + 1] = values[index];
        }

        return Array.AsReadOnly(result);
    }

    private static IReadOnlyList<SyntaxNode> CopyWithTail(
        IReadOnlyList<SyntaxNode> values,
        SyntaxNode tail)
    {
        var result = new SyntaxNode[values.Count + 1];
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        result[^1] = tail;
        return Array.AsReadOnly(result);
    }

    private readonly record struct TraversalFrame(SyntaxNode Node, int Depth, bool Exiting);
}

/// <summary>Rewrites immutable Expr trees and preserves unchanged node instances.</summary>
public abstract class SyntaxRewriter
{
    /// <summary>Gets or initializes the maximum tree depth accepted by this rewriter.</summary>
    public int MaximumDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_024;

    /// <summary>Recursively rewrites a node and its children.</summary>
    /// <param name="node">The node to rewrite.</param>
    /// <returns>The original node when unchanged, otherwise its replacement.</returns>
    public SyntaxNode Visit(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
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

            replacements?[index] = replacement;
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

            replacements?[index] = replacement;
        }

        return replacements is null ? nodes : SyntaxCollections.Copy(replacements);
    }
}
