using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Expr.Checking;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Patching;

/// <summary>Replaces a binary operator with the first compatible host function.</summary>
public sealed class OperatorOverridePatcher : IExprSemanticPatcher
{
    /// <summary>Initializes an operator override.</summary>
    /// <param name="operatorName">The binary operator spelling.</param>
    /// <param name="functionNames">Candidate function names in priority order.</param>
    public OperatorOverridePatcher(string operatorName, IEnumerable<string> functionNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);
        ArgumentNullException.ThrowIfNull(functionNames);
        string[] names = [.. functionNames];
        if (names.Length is 0 || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty function name is required.", nameof(functionNames));
        }

        OperatorName = operatorName;
        FunctionNames = new ReadOnlyCollection<string>(names);
    }

    /// <summary>Gets the operator spelling.</summary>
    public string OperatorName { get; }

    /// <summary>Gets candidate function names in priority order.</summary>
    public IReadOnlyList<string> FunctionNames { get; }

    /// <inheritdoc />
    public SyntaxNode Apply(SyntaxNode root, ExprSemanticModel model, ExprConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(configuration);
        return new Rewriter(this, model, configuration).Visit(root);
    }

    private sealed class Rewriter(
        OperatorOverridePatcher owner,
        ExprSemanticModel model,
        ExprConfiguration configuration) : SyntaxRewriter
    {
        protected override SyntaxNode VisitNode(SyntaxNode node)
        {
            if (node is not BinaryNode binary || !string.Equals(binary.Operator, owner.OperatorName, StringComparison.Ordinal))
            {
                return node;
            }

            if (!model.TryGetSemantics(binary.Left, out ExprNodeSemantics? leftSemantics) ||
                !model.TryGetSemantics(binary.Right, out ExprNodeSemantics? rightSemantics) ||
                leftSemantics is null || rightSemantics is null)
            {
                return node;
            }

            ExprTypeDescriptor left = leftSemantics.Type;
            ExprTypeDescriptor right = rightSemantics.Type;
            foreach (string name in owner.FunctionNames)
            {
                if (configuration.Functions.TryGetValue(name, out ExprFunction? function))
                {
                    foreach (ExprFunctionOverload overload in function.Overloads)
                    {
                        if (!overload.IsVariadic && overload.Parameters.Count == 2 &&
                            ExprTypeRelations.CanAssign(left, overload.Parameters[0]) &&
                            ExprTypeRelations.CanAssign(right, overload.Parameters[1]))
                        {
                            return Replace(binary, name);
                        }
                    }
                }

                if (configuration.Environment?.TryGetMember(name, out ExprEnvironmentMember? member) is true &&
                    member?.Type is FunctionTypeDescriptor environmentFunction &&
                    !environmentFunction.IsVariadic &&
                    environmentFunction.Parameters.Count is 2 &&
                    ExprTypeRelations.CanAssign(left, environmentFunction.Parameters[0]) &&
                    ExprTypeRelations.CanAssign(right, environmentFunction.Parameters[1]))
                {
                    return Replace(binary, name);
                }
            }

            return node;
        }

        private static SyntaxNode Replace(BinaryNode binary, string name)
        {
            var call = new CallNode(
                new IdentifierNode(name, binary.Location),
                [binary.Left, binary.Right],
                binary.Location);
            return Patch(binary, call);
        }
    }
}
