using System;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Syntax;

namespace Expr.Optimization;

internal sealed class ConstantFunctionRewriter(
    ExprConfiguration configuration,
    int maximumDepth) : OptimizationRewriter(maximumDepth)
{
    protected override SyntaxNode VisitNode(SyntaxNode node)
    {
        if (node is not CallNode { Callee: IdentifierNode identifier } call ||
            !configuration.ConstantFunctions.Contains(identifier.Name) ||
            !configuration.Functions.TryGetValue(identifier.Name, out ExprFunction? function))
        {
            return node;
        }

        object?[] arguments = new object?[call.Arguments.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!TryGetConstant(call.Arguments[index], out arguments[index]))
            {
                return node;
            }
        }

        try
        {
            ExprInvocationResult result = function.Invoke(arguments);
            if (configuration.MemoryBudget > 0 && result.MemoryCost > configuration.MemoryBudget)
            {
                throw new ExprOptimizationException(
                    $"constant function {function.Name} exceeded the configured memory budget",
                    node.Location);
            }

            return Replace(node, new ConstantNode(result.Value, node.Location));
        }
        catch (Exception exception) when (exception is not ExprOptimizationException)
        {
            throw new ExprOptimizationException(exception.Message, node.Location, exception);
        }
    }

    private static bool TryGetConstant(SyntaxNode node, out object? value)
    {
        switch (node)
        {
            case NilNode:
                value = null;
                return true;
            case IntegerNode integer:
                value = integer.Value;
                return true;
            case FloatNode number:
                value = number.Value;
                return true;
            case BooleanNode boolean:
                value = boolean.Value;
                return true;
            case StringNode text:
                value = text.Value;
                return true;
            case BytesNode bytes:
                value = bytes.Value.ToArray();
                return true;
            case ConstantNode constant:
                value = constant.Value;
                return true;
            default:
                value = null;
                return false;
        }
    }
}
