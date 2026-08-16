using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Checking;

/// <summary>Identifies how a statically resolved member will be accessed.</summary>
public enum ExprMemberBindingKind
{
    /// <summary>An environment-schema member.</summary>
    Environment,

    /// <summary>A CLR property or field.</summary>
    ClrMember,

    /// <summary>A CLR instance method.</summary>
    ClrMethod,

    /// <summary>An array or map index.</summary>
    Index,
}

/// <summary>Describes a statically selected member without mutating its syntax node.</summary>
/// <param name="Name">The expression-visible name.</param>
/// <param name="Kind">The binding kind.</param>
/// <param name="Member">The cached CLR member, when reflection supplied the binding.</param>
public sealed record ExprMemberBinding(string Name, ExprMemberBindingKind Kind, MemberInfo? Member = null);

/// <summary>Marks a value that must be unwrapped through <see cref="Patching.IExprValueProvider"/> before use.</summary>
/// <param name="ValueType">The statically declared unwrapped type.</param>
public sealed record ExprValueConversionBinding(ExprTypeDescriptor ValueType);

/// <summary>Contains static information inferred for one syntax node.</summary>
/// <param name="Type">The inferred result type.</param>
/// <param name="Function">The selected host or built-in function.</param>
/// <param name="Overload">The selected function overload.</param>
/// <param name="Member">The selected member binding.</param>
/// <param name="ValueConversion">The configured host-value conversion, when present.</param>
public sealed record ExprNodeSemantics(
    ExprTypeDescriptor Type,
    ExprFunction? Function = null,
    ExprFunctionOverload? Overload = null,
    ExprMemberBinding? Member = null,
    ExprValueConversionBinding? ValueConversion = null);

/// <summary>Provides immutable, reference-identity semantic annotations over an immutable syntax tree.</summary>
public sealed class ExprSemanticModel
{
    internal ExprSemanticModel(
        SyntaxTree syntaxTree,
        IReadOnlyDictionary<SyntaxNode, ExprNodeSemantics> annotations)
    {
        SyntaxTree = syntaxTree;
        var copy = new Dictionary<SyntaxNode, ExprNodeSemantics>(ReferenceEqualityComparer.Instance);
        foreach ((SyntaxNode node, ExprNodeSemantics semantics) in annotations)
        {
            copy.Add(node, semantics);
        }

        Annotations = new ReadOnlyDictionary<SyntaxNode, ExprNodeSemantics>(copy);
        ResultType = GetType(syntaxTree.Root);
    }

    /// <summary>Gets the checked, possibly semantically patched syntax tree.</summary>
    public SyntaxTree SyntaxTree { get; }

    /// <summary>Gets the root expression type.</summary>
    public ExprTypeDescriptor ResultType { get; }

    /// <summary>Gets all semantic annotations keyed by syntax-node identity.</summary>
    public IReadOnlyDictionary<SyntaxNode, ExprNodeSemantics> Annotations { get; }

    /// <summary>Gets the inferred type of a node.</summary>
    /// <param name="node">A node in <see cref="SyntaxTree"/>.</param>
    /// <returns>The inferred type.</returns>
    /// <exception cref="ArgumentException">The node does not belong to this model.</exception>
    public ExprTypeDescriptor GetType(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!Annotations.TryGetValue(node, out ExprNodeSemantics? semantics))
        {
            throw new ArgumentException("The syntax node does not belong to this semantic model.", nameof(node));
        }

        return semantics.Type;
    }

    /// <summary>Attempts to get the semantics of a node.</summary>
    /// <param name="node">The syntax node.</param>
    /// <param name="semantics">The inferred semantics.</param>
    /// <returns><see langword="true"/> when the node belongs to this model.</returns>
    public bool TryGetSemantics(SyntaxNode node, out ExprNodeSemantics? semantics)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Annotations.TryGetValue(node, out semantics);
    }
}
