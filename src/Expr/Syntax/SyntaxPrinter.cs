using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Expr.Syntax;

/// <summary>Configures canonical Expr syntax printing.</summary>
public sealed record SyntaxPrinterOptions
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
    } = 1_024;

    /// <summary>Gets or initializes the maximum number of visited nodes.</summary>
    public int MaximumNodeCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 100_000;
}

/// <summary>Prints immutable syntax trees as deterministic, canonical Expr source.</summary>
public static class SyntaxPrinter
{
    private static readonly IReadOnlyDictionary<string, OperatorInfo> BinaryOperators =
        new Dictionary<string, OperatorInfo>(StringComparer.Ordinal)
        {
            ["|"] = new(0, false),
            ["or"] = new(10, false),
            ["||"] = new(10, false),
            ["and"] = new(15, false),
            ["&&"] = new(15, false),
            ["=="] = new(20, false),
            ["!="] = new(20, false),
            ["<"] = new(20, false),
            [">"] = new(20, false),
            [">="] = new(20, false),
            ["<="] = new(20, false),
            ["in"] = new(20, false),
            ["matches"] = new(20, false),
            ["contains"] = new(20, false),
            ["startsWith"] = new(20, false),
            ["endsWith"] = new(20, false),
            [".."] = new(25, false),
            ["+"] = new(30, false),
            ["-"] = new(30, false),
            ["*"] = new(60, false),
            ["/"] = new(60, false),
            ["%"] = new(60, false),
            ["**"] = new(100, true),
            ["^"] = new(100, true),
            ["??"] = new(500, false),
        };

    private static readonly IReadOnlyDictionary<string, int> UnaryOperators =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["not"] = 50,
            ["!"] = 50,
            ["-"] = 90,
            ["+"] = 90,
        };

    /// <summary>Prints a syntax node as canonical Expr source.</summary>
    /// <param name="node">The syntax node.</param>
    /// <param name="options">Optional traversal limits.</param>
    /// <returns>Canonical, culture-independent Expr source.</returns>
    public static string Print(SyntaxNode node, SyntaxPrinterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        var printer = new Printer(options ?? new SyntaxPrinterOptions());
        return printer.Print(node);
    }

    private static bool IsBoolean(string value) => value is "and" or "or" or "&&" or "||";

    private static bool IsValidIdentifier(string value)
    {
        var runes = value.EnumerateRunes().ToArray();
        return runes.Length > 0
            && IsAlphabetic(runes[0])
            && runes.Skip(1).All(static rune => IsAlphabetic(rune) || Rune.IsDigit(rune));
    }

    private static bool IsAlphabetic(Rune value) => value.Value is '_' or '$' || Rune.IsLetter(value);

    private static void AppendQuotedString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\a':
                    builder.Append("\\a");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\v':
                    builder.Append("\\v");
                    break;
                default:
                    if (character is < ' ' or '\u007f')
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\x{(int)character:x2}");
                    }
                    else if (char.IsSurrogate(character)
                        && (index + 1 >= value.Length || !char.IsSurrogatePair(character, value[index + 1])))
                    {
                        throw new NotSupportedException("Expr strings cannot represent unpaired UTF-16 surrogates.");
                    }
                    else
                    {
                        builder.Append(character);
                        if (char.IsHighSurrogate(character))
                        {
                            builder.Append(value[++index]);
                        }
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static void AppendQuotedBytes(StringBuilder builder, ReadOnlySpan<byte> value)
    {
        builder.Append("b\"");
        foreach (var item in value)
        {
            switch (item)
            {
                case (byte)'"':
                    builder.Append("\\\"");
                    break;
                case (byte)'\\':
                    builder.Append("\\\\");
                    break;
                case 0x07:
                    builder.Append("\\a");
                    break;
                case 0x08:
                    builder.Append("\\b");
                    break;
                case 0x0c:
                    builder.Append("\\f");
                    break;
                case 0x0a:
                    builder.Append("\\n");
                    break;
                case 0x0d:
                    builder.Append("\\r");
                    break;
                case 0x09:
                    builder.Append("\\t");
                    break;
                case 0x0b:
                    builder.Append("\\v");
                    break;
                default:
                    if (item is < 0x20 or >= 0x7f)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\x{item:x2}");
                    }
                    else
                    {
                        builder.Append((char)item);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private sealed class Printer
    {
        private readonly int maximumDepth;
        private readonly int maximumNodeCount;
        private readonly HashSet<object> constantPath = new(ReferenceEqualityComparer.Instance);
        private int nodeCount;

        internal Printer(SyntaxPrinterOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            maximumDepth = options.MaximumDepth;
            maximumNodeCount = options.MaximumNodeCount;
        }

        internal string Print(SyntaxNode node)
        {
            var builder = new StringBuilder();
            AppendNode(builder, node, 0);
            return builder.ToString();
        }

        private void AppendNode(StringBuilder builder, SyntaxNode node, int depth)
        {
            Enter(node, depth);
            switch (node)
            {
                case NilNode:
                    builder.Append("nil");
                    break;
                case IdentifierNode identifier:
                    builder.Append(identifier.Name);
                    break;
                case IntegerNode integer:
                    builder.Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                    break;
                case FloatNode number:
                    AppendFloatingPoint(builder, number.Value);
                    break;
                case BooleanNode boolean:
                    builder.Append(boolean.Value ? "true" : "false");
                    break;
                case StringNode text:
                    AppendQuotedString(builder, text.Value);
                    break;
                case BytesNode bytes:
                    AppendQuotedBytes(builder, bytes.Value.Span);
                    break;
                case ConstantNode constant:
                    AppendConstant(builder, constant.Value, depth + 1);
                    break;
                case UnaryNode unary:
                    AppendUnary(builder, unary, depth);
                    break;
                case BinaryNode binary:
                    AppendBinary(builder, binary, depth);
                    break;
                case ChainNode chain:
                    AppendNode(builder, chain.Expression, depth + 1);
                    break;
                case MemberNode member:
                    AppendMember(builder, member, depth);
                    break;
                case SliceNode slice:
                    AppendPostfixTarget(builder, slice.Target, depth + 1);
                    builder.Append('[');
                    if (slice.From is not null)
                    {
                        AppendGroupedExpression(builder, slice.From, depth + 1);
                    }

                    builder.Append(':');
                    if (slice.To is not null)
                    {
                        AppendGroupedExpression(builder, slice.To, depth + 1);
                    }

                    builder.Append(']');
                    break;
                case CallNode call:
                    AppendPostfixTarget(builder, call.Callee, depth + 1);
                    AppendArguments(builder, call.Arguments, depth);
                    break;
                case BuiltinNode builtin:
                    builder.Append(builtin.Name);
                    AppendArguments(builder, builtin.Arguments, depth);
                    break;
                case PredicateNode predicate:
                    if (predicate.Body is SequenceNode)
                    {
                        builder.Append('{');
                        AppendNode(builder, predicate.Body, depth + 1);
                        builder.Append('}');
                    }
                    else
                    {
                        AppendNode(builder, predicate.Body, depth + 1);
                    }

                    break;
                case PointerNode pointer:
                    builder.Append('#');
                    builder.Append(pointer.Name);
                    break;
                case ConditionalNode conditional:
                    AppendConditional(builder, conditional, depth);
                    break;
                case VariableDeclaratorNode variable:
                    builder.Append("let ");
                    builder.Append(variable.Name);
                    builder.Append(" = ");
                    AppendGroupedExpression(builder, variable.Value, depth + 1);
                    builder.Append("; ");
                    AppendNode(builder, variable.Body, depth + 1);
                    break;
                case SequenceNode sequence:
                    AppendSeparated(builder, sequence.Expressions, "; ", depth);
                    break;
                case ArrayNode array:
                    builder.Append('[');
                    AppendSeparated(builder, array.Elements, ", ", depth);
                    builder.Append(']');
                    break;
                case MapNode map:
                    builder.Append('{');
                    AppendSeparated(builder, map.Pairs, ", ", depth);
                    builder.Append('}');
                    break;
                case PairNode pair:
                    AppendPair(builder, pair, depth);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(node), node.GetType(), "Unknown syntax node type.");
            }
        }

        private void AppendUnary(StringBuilder builder, UnaryNode unary, int depth)
        {
            if (!UnaryOperators.TryGetValue(unary.Operator, out var precedence))
            {
                throw new InvalidOperationException($"Unknown unary operator '{unary.Operator}'.");
            }

            builder.Append(unary.Operator);
            if (unary.Operator == "not")
            {
                builder.Append(' ');
            }

            var wrap = unary.Operand is ConditionalNode or VariableDeclaratorNode or SequenceNode
                || unary.Operand is BinaryNode binary
                && GetBinaryOperator(binary.Operator).Precedence < precedence;
            AppendPossiblyParenthesized(builder, unary.Operand, wrap, depth + 1);
        }

        private void AppendBinary(StringBuilder builder, BinaryNode binary, int depth)
        {
            if (binary.Operator == "..")
            {
                var range = GetBinaryOperator(binary.Operator);
                var rangeLeftWrap = binary.Left is ConditionalNode or VariableDeclaratorNode or SequenceNode
                    || binary.Left is BinaryNode left && GetBinaryOperator(left.Operator).Precedence < range.Precedence;
                var rangeRightWrap = binary.Right is ConditionalNode or VariableDeclaratorNode or SequenceNode
                    || binary.Right is BinaryNode right && GetBinaryOperator(right.Operator).Precedence <= range.Precedence;
                AppendPossiblyParenthesized(builder, binary.Left, rangeLeftWrap, depth + 1);
                builder.Append("..");
                AppendPossiblyParenthesized(builder, binary.Right, rangeRightWrap, depth + 1);
                return;
            }

            var current = GetBinaryOperator(binary.Operator);
            var leftWrap = binary.Left is ConditionalNode or VariableDeclaratorNode or SequenceNode;
            if (binary.Left is UnaryNode leftUnary)
            {
                leftWrap |= GetUnaryPrecedence(leftUnary.Operator) < current.Precedence;
            }

            if (binary.Left is BinaryNode leftBinary)
            {
                var left = GetBinaryOperator(leftBinary.Operator);
                leftWrap |= left.Precedence < current.Precedence
                    || left.Precedence == current.Precedence && current.RightAssociative
                    || leftBinary.Operator == "??"
                    || IsBoolean(leftBinary.Operator) && leftBinary.Operator != binary.Operator;
            }

            var rightWrap = binary.Right is ConditionalNode or VariableDeclaratorNode or SequenceNode;
            if (binary.Right is BinaryNode rightBinary)
            {
                var right = GetBinaryOperator(rightBinary.Operator);
                rightWrap |= right.Precedence < current.Precedence
                    || right.Precedence == current.Precedence && !current.RightAssociative
                    || IsBoolean(rightBinary.Operator) && rightBinary.Operator != binary.Operator;
            }

            AppendPossiblyParenthesized(builder, binary.Left, leftWrap, depth + 1);
            builder.Append(' ');
            builder.Append(binary.Operator);
            builder.Append(' ');
            AppendPossiblyParenthesized(builder, binary.Right, rightWrap, depth + 1);
        }

        private void AppendMember(StringBuilder builder, MemberNode member, int depth)
        {
            var validName = member.Property is StringNode property && IsValidIdentifier(property.Value);
            if (member.Target is PointerNode { Name.Length: 0 } && validName && !member.Optional)
            {
                builder.Append('.');
                builder.Append(((StringNode)member.Property).Value);
                return;
            }

            AppendPostfixTarget(builder, member.Target, depth + 1);
            if (validName)
            {
                builder.Append(member.Optional ? "?." : ".");
                builder.Append(((StringNode)member.Property).Value);
                return;
            }

            builder.Append(member.Optional ? "?.[" : "[");
            AppendGroupedExpression(builder, member.Property, depth + 1);
            builder.Append(']');
        }

        private void AppendConditional(StringBuilder builder, ConditionalNode conditional, int depth)
        {
            if (!conditional.IsTernary)
            {
                builder.Append("if ");
                AppendGroupedExpression(builder, conditional.Condition, depth + 1);
                builder.Append(" { ");
                AppendNode(builder, conditional.WhenTrue, depth + 1);
                builder.Append(" } else ");
                if (conditional.WhenFalse is ConditionalNode { IsTernary: false })
                {
                    AppendNode(builder, conditional.WhenFalse, depth + 1);
                }
                else
                {
                    builder.Append("{ ");
                    AppendNode(builder, conditional.WhenFalse, depth + 1);
                    builder.Append(" }");
                }

                return;
            }

            AppendPossiblyParenthesized(
                builder,
                conditional.Condition,
                conditional.Condition is ConditionalNode or VariableDeclaratorNode or SequenceNode,
                depth + 1);
            builder.Append(" ? ");
            AppendPossiblyParenthesized(
                builder,
                conditional.WhenTrue,
                conditional.WhenTrue is ConditionalNode or VariableDeclaratorNode or SequenceNode,
                depth + 1);
            builder.Append(" : ");
            AppendPossiblyParenthesized(
                builder,
                conditional.WhenFalse,
                conditional.WhenFalse is ConditionalNode or VariableDeclaratorNode or SequenceNode,
                depth + 1);
        }

        private void AppendPair(StringBuilder builder, PairNode pair, int depth)
        {
            if (pair.Key is StringNode key)
            {
                if (IsValidIdentifier(key.Value))
                {
                    builder.Append(key.Value);
                }
                else
                {
                    AppendQuotedString(builder, key.Value);
                }
            }
            else
            {
                builder.Append('(');
                AppendNode(builder, pair.Key, depth + 1);
                builder.Append(')');
            }

            builder.Append(": ");
            AppendGroupedExpression(builder, pair.Value, depth + 1);
        }

        private void AppendArguments(StringBuilder builder, IReadOnlyList<SyntaxNode> arguments, int depth)
        {
            builder.Append('(');
            AppendSeparated(builder, arguments, ", ", depth);
            builder.Append(')');
        }

        private void AppendSeparated<T>(StringBuilder builder, IReadOnlyList<T> nodes, string separator, int depth)
            where T : SyntaxNode
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(separator);
                }

                AppendGroupedExpression(builder, nodes[index], depth + 1);
            }
        }

        private void AppendGroupedExpression(StringBuilder builder, SyntaxNode node, int depth) =>
            AppendPossiblyParenthesized(
                builder,
                node,
                node is VariableDeclaratorNode or SequenceNode,
                depth);

        private void AppendPostfixTarget(StringBuilder builder, SyntaxNode target, int depth)
        {
            var wrap = target is NilNode or IntegerNode or FloatNode or BooleanNode or ConstantNode or UnaryNode or
                BinaryNode or ConditionalNode or VariableDeclaratorNode or SequenceNode;
            AppendPossiblyParenthesized(builder, target, wrap, depth);
        }

        private void AppendPossiblyParenthesized(StringBuilder builder, SyntaxNode node, bool wrap, int depth)
        {
            if (wrap)
            {
                builder.Append('(');
            }

            AppendNode(builder, node, depth);
            if (wrap)
            {
                builder.Append(')');
            }
        }

        private void AppendConstant(StringBuilder builder, object? value, int depth)
        {
            if (depth >= maximumDepth)
            {
                throw new InvalidOperationException($"Constant exceeds the configured printer depth limit of {maximumDepth}.");
            }

            nodeCount++;
            if (nodeCount > maximumNodeCount)
            {
                throw new InvalidOperationException(
                    $"Syntax tree exceeds the configured printer node limit of {maximumNodeCount}.");
            }

            switch (value)
            {
                case null:
                    builder.Append("nil");
                    break;
                case bool boolean:
                    builder.Append(boolean ? "true" : "false");
                    break;
                case string text:
                    AppendQuotedString(builder, text);
                    break;
                case char character:
                    AppendQuotedString(builder, character.ToString());
                    break;
                case byte[] bytes:
                    AppendQuotedBytes(builder, bytes);
                    break;
                case ReadOnlyMemory<byte> bytes:
                    AppendQuotedBytes(builder, bytes.Span);
                    break;
                case ulong integer when integer > long.MaxValue:
                    throw new NotSupportedException(
                        "Unsigned integer constants above Int64.MaxValue cannot be represented as Expr source.");
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case float number:
                    AppendFloatingPoint(builder, number);
                    break;
                case double number:
                    AppendFloatingPoint(builder, number);
                    break;
                case decimal number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case IDictionary dictionary:
                    AppendConstantMap(builder, dictionary, depth);
                    break;
                case IEnumerable sequence:
                    AppendConstantSequence(builder, sequence, depth);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Constant value type '{value.GetType().FullName}' cannot be represented as Expr source.");
            }
        }

        private void AppendConstantMap(StringBuilder builder, IDictionary dictionary, int depth)
        {
            EnterConstantContainer(dictionary);
            try
            {
                var entries = new List<(string Key, object? Value)>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key)
                    {
                        throw new NotSupportedException("Only string-keyed constant maps can be represented as Expr source.");
                    }

                    entries.Add((key, entry.Value));
                }

                entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
                builder.Append('{');
                for (var index = 0; index < entries.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    AppendQuotedString(builder, entries[index].Key);
                    builder.Append(':');
                    AppendConstant(builder, entries[index].Value, depth + 1);
                }

                builder.Append('}');
            }
            finally
            {
                _ = constantPath.Remove(dictionary);
            }
        }

        private void AppendConstantSequence(StringBuilder builder, IEnumerable sequence, int depth)
        {
            EnterConstantContainer(sequence);
            try
            {
                builder.Append('[');
                var first = true;
                foreach (var value in sequence)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    AppendConstant(builder, value, depth + 1);
                    first = false;
                }

                builder.Append(']');
            }
            finally
            {
                _ = constantPath.Remove(sequence);
            }
        }

        private static void AppendFloatingPoint(StringBuilder builder, float value)
        {
            if (!float.IsFinite(value))
            {
                throw new NotSupportedException("Non-finite floating-point constants cannot be represented as Expr source.");
            }

            AppendFloatingPointText(builder, value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendFloatingPoint(StringBuilder builder, double value)
        {
            if (!double.IsFinite(value))
            {
                throw new NotSupportedException("Non-finite floating-point constants cannot be represented as Expr source.");
            }

            AppendFloatingPointText(builder, value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendFloatingPointText(StringBuilder builder, string value)
        {
            builder.Append(value);
            if (!value.Contains('.', StringComparison.Ordinal)
                && !value.Contains('e', StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(".0");
            }
        }

        private void Enter(SyntaxNode node, int depth)
        {
            if (depth >= maximumDepth)
            {
                throw new InvalidOperationException($"Syntax tree exceeds the configured printer depth limit of {maximumDepth}.");
            }

            nodeCount++;
            if (nodeCount > maximumNodeCount)
            {
                throw new InvalidOperationException(
                    $"Syntax tree exceeds the configured printer node limit of {maximumNodeCount}.");
            }

        }

        private void EnterConstantContainer(object value)
        {
            if (!constantPath.Add(value))
            {
                throw new InvalidOperationException("Constant value contains a reference cycle.");
            }
        }

        private static OperatorInfo GetBinaryOperator(string value) => BinaryOperators.TryGetValue(value, out var info)
            ? info
            : throw new InvalidOperationException($"Unknown binary operator '{value}'.");

        private static int GetUnaryPrecedence(string value) => UnaryOperators.TryGetValue(value, out var precedence)
            ? precedence
            : throw new InvalidOperationException($"Unknown unary operator '{value}'.");
    }

    private readonly record struct OperatorInfo(int Precedence, bool RightAssociative);
}
