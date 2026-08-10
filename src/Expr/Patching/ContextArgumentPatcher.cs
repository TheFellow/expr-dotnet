using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Expr.Checking;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Patching;

/// <summary>Injects an environment cancellation token into compatible host calls.</summary>
public sealed class ContextArgumentPatcher : IExprSemanticPatcher
{
    /// <summary>Initializes a context patcher.</summary>
    /// <param name="environmentName">The environment variable holding the context.</param>
    public ContextArgumentPatcher(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        EnvironmentName = environmentName;
    }

    /// <summary>Gets the environment variable name.</summary>
    public string EnvironmentName { get; }

    /// <inheritdoc />
    public SyntaxNode Apply(SyntaxNode root, ExprSemanticModel model, ExprConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(configuration);
        return new Rewriter(EnvironmentName, model).Visit(root);
    }

    private sealed class Rewriter(string environmentName, ExprSemanticModel model) : SyntaxRewriter
    {
        protected override SyntaxNode VisitNode(SyntaxNode node)
        {
            if (node is not CallNode call || HasContextArgument(call))
            {
                return node;
            }

            ExprFunction? function = null;
            if (model.TryGetSemantics(call, out ExprNodeSemantics? callSemantics))
            {
                function = callSemantics?.Function;
            }

            if (function is null && model.TryGetSemantics(call.Callee, out ExprNodeSemantics? calleeSemantics))
            {
                function = calleeSemantics?.Function;
                if (calleeSemantics?.Member?.Member is MethodInfo method &&
                    ContextInsertionIndex(method, call.Arguments.Count) is int methodIndex)
                {
                    return AddContext(call, methodIndex);
                }
            }

            ExprFunctionOverload? contextualOverload = function?.Overloads.FirstOrDefault(
                overload => ContextInsertionIndex(overload, call.Arguments.Count) is not null);
            if (contextualOverload is null)
            {
                return node;
            }

            return AddContext(call, ContextInsertionIndex(contextualOverload, call.Arguments.Count)!.Value);
        }

        private bool HasContextArgument(CallNode call) =>
            call.Arguments.Count > 0 &&
            (call.Arguments[0] is IdentifierNode first &&
                string.Equals(first.Name, environmentName, StringComparison.Ordinal) ||
             call.Arguments[^1] is IdentifierNode last &&
                string.Equals(last.Name, environmentName, StringComparison.Ordinal));

        private SyntaxNode AddContext(CallNode call, int insertionIndex)
        {
            var arguments = call.Arguments.ToList();
            arguments.Insert(insertionIndex, new IdentifierNode(environmentName, call.Location));
            return Patch(call, new CallNode(call.Callee, arguments, call.Location));
        }

        private static int? ContextInsertionIndex(ExprFunctionOverload overload, int argumentCount)
        {
            if (overload.Parameters.Count is 0)
            {
                return null;
            }

            if (IsCancellationToken(overload.Parameters[0]) &&
                (overload.IsVariadic
                    ? argumentCount >= overload.Parameters.Count - 2
                    : argumentCount == overload.Parameters.Count - 1))
            {
                return 0;
            }

            return !overload.IsVariadic &&
                IsCancellationToken(overload.Parameters[^1]) &&
                argumentCount == overload.Parameters.Count - 1
                ? argumentCount
                : null;
        }

        private static int? ContextInsertionIndex(MethodInfo method, int argumentCount)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length is 0)
            {
                return null;
            }

            bool variadic = parameters[^1].GetCustomAttribute<ParamArrayAttribute>() is not null;
            if (parameters[0].ParameterType == typeof(CancellationToken) &&
                (variadic ? argumentCount >= parameters.Length - 2 : argumentCount == parameters.Length - 1))
            {
                return 0;
            }

            return !variadic &&
                parameters[^1].ParameterType == typeof(CancellationToken) &&
                argumentCount == parameters.Length - 1
                ? argumentCount
                : null;
        }

        private static bool IsCancellationToken(ExprTypeDescriptor type) =>
            type is ObjectTypeDescriptor objectType && objectType.ClrType == typeof(CancellationToken);
    }
}
