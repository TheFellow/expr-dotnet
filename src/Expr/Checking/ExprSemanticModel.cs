using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Expr.Configuration;
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
public sealed record ExprMemberBinding
{
    /// <summary>Initializes a member binding.</summary>
    public ExprMemberBinding(string name, ExprMemberBindingKind kind, MemberInfo? member = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown member binding kind.");
        }

        if (kind is ExprMemberBindingKind.ClrMember or ExprMemberBindingKind.ClrMethod && member is null)
        {
            throw new ArgumentException("CLR member bindings require reflection metadata.", nameof(member));
        }

        if (kind is not (ExprMemberBindingKind.ClrMember or ExprMemberBindingKind.ClrMethod) && member is not null)
        {
            throw new ArgumentException("Only CLR member bindings can carry reflection metadata.", nameof(member));
        }

        Name = name;
        Kind = kind;
        Member = member;
    }

    /// <summary>Gets the expression-visible name.</summary>
    public string Name { get; }

    /// <summary>Gets the binding kind.</summary>
    public ExprMemberBindingKind Kind { get; }

    /// <summary>Gets the cached CLR member, when reflection supplied the binding.</summary>
    public MemberInfo? Member { get; }
}

/// <summary>Marks a value that must be unwrapped through <see cref="Patching.IExprValueProvider"/> before use.</summary>
public sealed record ExprValueConversionBinding
{
    /// <summary>Initializes a value-conversion binding.</summary>
    public ExprValueConversionBinding(ExprTypeDescriptor valueType) =>
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));

    /// <summary>Gets the statically declared unwrapped type.</summary>
    public ExprTypeDescriptor ValueType { get; }
}

/// <summary>Contains static information inferred for one syntax node.</summary>
public sealed record ExprNodeSemantics
{
    /// <summary>Initializes checked semantics for one node.</summary>
    public ExprNodeSemantics(
        ExprTypeDescriptor type,
        ExprFunction? function = null,
        ExprFunctionOverload? overload = null,
        ExprMemberBinding? member = null,
        ExprValueConversionBinding? valueConversion = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        if (overload is not null)
        {
            if (function is null)
            {
                throw new ArgumentException("An overload cannot be selected without a function.", nameof(overload));
            }

            if (!function.Overloads.Contains(overload))
            {
                throw new ArgumentException("The selected overload does not belong to the selected function.", nameof(overload));
            }
        }

        Function = function;
        Overload = overload;
        Member = member;
        ValueConversion = valueConversion;
    }

    /// <summary>Gets the inferred result type.</summary>
    public ExprTypeDescriptor Type { get; }

    /// <summary>Gets the selected function.</summary>
    public ExprFunction? Function { get; }

    /// <summary>Gets the selected function overload.</summary>
    public ExprFunctionOverload? Overload { get; }

    /// <summary>Gets the selected member binding.</summary>
    public ExprMemberBinding? Member { get; }

    /// <summary>Gets the configured host-value conversion.</summary>
    public ExprValueConversionBinding? ValueConversion { get; }
}

/// <summary>Provides immutable, reference-identity semantic annotations over an immutable syntax tree.</summary>
public sealed class ExprSemanticModel
{
    internal ExprSemanticModel(
        SyntaxTree syntaxTree,
        IReadOnlyDictionary<SyntaxNode, ExprNodeSemantics> annotations,
        ExprConfiguration configuration)
    {
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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

    /// <summary>Gets the immutable configuration under which this model was checked.</summary>
    public ExprConfiguration Configuration { get; }

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
