using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Expr.Syntax;

/// <summary>Configures diagnostic syntax-tree dumps.</summary>
public sealed record SyntaxDumperOptions
{
    /// <summary>Gets or initializes whether scalar source ranges are included.</summary>
    public bool IncludeLocations { get; init; }

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

/// <summary>Produces deterministic, reflection-free diagnostic syntax-tree dumps.</summary>
public static class SyntaxDumper
{
    /// <summary>Dumps a syntax tree with stable node and property names.</summary>
    /// <param name="node">The syntax node.</param>
    /// <param name="options">Optional dump settings.</param>
    /// <returns>A multiline diagnostic representation.</returns>
    public static string Dump(SyntaxNode node, SyntaxDumperOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new Dumper(options ?? new SyntaxDumperOptions()).Dump(node);
    }

    private sealed class Dumper
    {
        private readonly bool includeLocations;
        private readonly int maximumDepth;
        private readonly int maximumNodeCount;
        private readonly StringBuilder builder = new();
        private int nodeCount;

        internal Dumper(SyntaxDumperOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            includeLocations = options.IncludeLocations;
            maximumDepth = options.MaximumDepth;
            maximumNodeCount = options.MaximumNodeCount;
        }

        internal string Dump(SyntaxNode node)
        {
            AppendNode(node, 0);
            return builder.ToString();
        }

        private void AppendNode(SyntaxNode node, int depth)
        {
            Enter(node, depth);
            Indent(depth);
            builder.Append(node.GetType().Name);
            builder.AppendLine(" {");
            if (includeLocations)
            {
                AppendScalar(depth + 1, "Location", $"{node.Location.Start}..{node.Location.End}", quote: false);
            }

            switch (node)
            {
                case NilNode:
                    break;
                case IdentifierNode identifier:
                    AppendScalar(depth + 1, "Name", identifier.Name);
                    break;
                case IntegerNode integer:
                    AppendScalar(depth + 1, "Value", integer.Value.ToString(CultureInfo.InvariantCulture), quote: false);
                    break;
                case FloatNode number:
                    AppendScalar(depth + 1, "Value", number.Value.ToString("R", CultureInfo.InvariantCulture), quote: false);
                    break;
                case BooleanNode boolean:
                    AppendScalar(depth + 1, "Value", boolean.Value ? "true" : "false", quote: false);
                    break;
                case StringNode text:
                    AppendScalar(depth + 1, "Value", text.Value);
                    break;
                case BytesNode bytes:
                    AppendScalar(depth + 1, "Value", Convert.ToHexString(bytes.Value.Span), quote: false);
                    break;
                case ConstantNode constant:
                    AppendScalar(depth + 1, "Value", FormatConstant(constant.Value), quote: false);
                    break;
                case UnaryNode unary:
                    AppendScalar(depth + 1, "Operator", unary.Operator);
                    AppendChild(depth + 1, "Operand", unary.Operand);
                    break;
                case BinaryNode binary:
                    AppendScalar(depth + 1, "Operator", binary.Operator);
                    AppendChild(depth + 1, "Left", binary.Left);
                    AppendChild(depth + 1, "Right", binary.Right);
                    break;
                case ChainNode chain:
                    AppendChild(depth + 1, "Expression", chain.Expression);
                    break;
                case MemberNode member:
                    AppendChild(depth + 1, "Target", member.Target);
                    AppendChild(depth + 1, "Property", member.Property);
                    AppendScalar(depth + 1, "Optional", member.Optional ? "true" : "false", quote: false);
                    AppendScalar(depth + 1, "IsMethod", member.IsMethod ? "true" : "false", quote: false);
                    break;
                case SliceNode slice:
                    AppendChild(depth + 1, "Target", slice.Target);
                    AppendOptionalChild(depth + 1, "From", slice.From);
                    AppendOptionalChild(depth + 1, "To", slice.To);
                    break;
                case CallNode call:
                    AppendChild(depth + 1, "Callee", call.Callee);
                    AppendChildren(depth + 1, "Arguments", call.Arguments);
                    break;
                case BuiltinNode builtin:
                    AppendScalar(depth + 1, "Name", builtin.Name);
                    AppendChildren(depth + 1, "Arguments", builtin.Arguments);
                    AppendScalar(depth + 1, "Throws", builtin.Throws ? "true" : "false", quote: false);
                    AppendOptionalChild(depth + 1, "Map", builtin.Map);
                    AppendScalar(
                        depth + 1,
                        "Threshold",
                        builtin.Threshold?.ToString(CultureInfo.InvariantCulture) ?? "null",
                        quote: false);
                    break;
                case PredicateNode predicate:
                    AppendChild(depth + 1, "Body", predicate.Body);
                    break;
                case PointerNode pointer:
                    AppendScalar(depth + 1, "Name", pointer.Name);
                    break;
                case ConditionalNode conditional:
                    AppendChild(depth + 1, "Condition", conditional.Condition);
                    AppendChild(depth + 1, "WhenTrue", conditional.WhenTrue);
                    AppendChild(depth + 1, "WhenFalse", conditional.WhenFalse);
                    AppendScalar(depth + 1, "IsTernary", conditional.IsTernary ? "true" : "false", quote: false);
                    break;
                case VariableDeclaratorNode variable:
                    AppendScalar(depth + 1, "Name", variable.Name);
                    AppendChild(depth + 1, "Value", variable.Value);
                    AppendChild(depth + 1, "Body", variable.Body);
                    break;
                case SequenceNode sequence:
                    AppendChildren(depth + 1, "Expressions", sequence.Expressions);
                    break;
                case ArrayNode array:
                    AppendChildren(depth + 1, "Elements", array.Elements);
                    break;
                case MapNode map:
                    AppendChildren(depth + 1, "Pairs", map.Pairs);
                    break;
                case PairNode pair:
                    AppendChild(depth + 1, "Key", pair.Key);
                    AppendChild(depth + 1, "Value", pair.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(node), node.GetType(), "Unknown syntax node type.");
            }

            Indent(depth);
            builder.Append('}');
        }

        private void AppendChild(int depth, string name, SyntaxNode node)
        {
            Indent(depth);
            builder.Append(name);
            builder.AppendLine(":");
            AppendNode(node, depth + 1);
            builder.AppendLine();
        }

        private void AppendOptionalChild(int depth, string name, SyntaxNode? node)
        {
            if (node is not null)
            {
                AppendChild(depth, name, node);
                return;
            }

            AppendScalar(depth, name, "null", quote: false);
        }

        private void AppendChildren<T>(int depth, string name, IReadOnlyList<T> nodes)
            where T : SyntaxNode
        {
            Indent(depth);
            builder.Append(name);
            if (nodes.Count == 0)
            {
                builder.AppendLine(": []");
                return;
            }

            builder.AppendLine(": [");
            foreach (var node in nodes)
            {
                AppendNode(node, depth + 1);
                builder.AppendLine();
            }

            Indent(depth);
            builder.AppendLine("]");
        }

        private void AppendScalar(int depth, string name, string value, bool quote = true)
        {
            Indent(depth);
            builder.Append(name);
            builder.Append(": ");
            if (quote)
            {
                AppendQuoted(value);
            }
            else
            {
                builder.Append(value);
            }

            builder.AppendLine();
        }

        private void AppendQuoted(string value)
        {
            builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
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
                    default:
                        builder.Append(character);
                        break;
                }
            }

            builder.Append('"');
        }

        private static string FormatConstant(object? value) => value switch
        {
            null => "null",
            string text => $"String({text.Length.ToString(CultureInfo.InvariantCulture)} chars)",
            byte[] bytes => $"Byte[{bytes.Length.ToString(CultureInfo.InvariantCulture)}]({Convert.ToHexString(bytes)})",
            Array array => $"{array.GetType().GetElementType()?.Name ?? "Object"}[{array.Length.ToString(CultureInfo.InvariantCulture)}]",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "null",
            _ => value.GetType().FullName ?? value.GetType().Name,
        };

        private void Enter(SyntaxNode node, int depth)
        {
            if (depth >= maximumDepth)
            {
                throw new InvalidOperationException($"Syntax tree exceeds the configured dump depth limit of {maximumDepth}.");
            }

            nodeCount++;
            if (nodeCount > maximumNodeCount)
            {
                throw new InvalidOperationException(
                    $"Syntax tree exceeds the configured dump node limit of {maximumNodeCount}.");
            }

        }

        private void Indent(int depth) => builder.Append(' ', depth * 2);
    }
}
