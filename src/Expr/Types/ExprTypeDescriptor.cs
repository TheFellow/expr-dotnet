using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Expr.Types;

/// <summary>
/// Describes an Expr value type independently of checker or syntax-tree state.
/// </summary>
public abstract record ExprTypeDescriptor
{
    /// <summary>
    /// Initializes a type descriptor.
    /// </summary>
    /// <param name="kind">The semantic type category.</param>
    /// <param name="displayName">The diagnostic display name.</param>
    protected ExprTypeDescriptor(ExprTypeKind kind, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Kind = kind;
        DisplayName = displayName;
    }

    /// <summary>Gets the semantic type category.</summary>
    public ExprTypeKind Kind { get; }

    /// <summary>Gets the stable name used in diagnostics.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Determines whether this descriptor accepts the other descriptor.
    /// </summary>
    /// <remarks>
    /// As in Expr's Go type facade, <c>any</c> is a wildcard on either side.
    /// </remarks>
    /// <param name="other">The descriptor to compare.</param>
    /// <returns><see langword="true"/> when the descriptors are semantically equivalent.</returns>
    public virtual bool IsEquivalentTo(ExprTypeDescriptor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Kind is ExprTypeKind.Any || other.Kind is ExprTypeKind.Any || Equals(other);
    }

    /// <inheritdoc />
    public sealed override string ToString() => DisplayName;
}

/// <summary>Describes a scalar Expr type.</summary>
public sealed record PrimitiveTypeDescriptor : ExprTypeDescriptor
{
    internal PrimitiveTypeDescriptor(ExprTypeKind kind, string displayName, Type? clrType = null)
        : base(kind, displayName)
    {
        ClrType = clrType;
    }

    /// <summary>Gets the canonical CLR representation, when one exists.</summary>
    public Type? ClrType { get; }
}

/// <summary>Describes a value that may be either <see langword="null"/> or a specific Expr type.</summary>
public sealed record NullableTypeDescriptor : ExprTypeDescriptor
{
    /// <summary>Initializes a nullable descriptor.</summary>
    /// <param name="underlyingType">The non-null semantic type.</param>
    public NullableTypeDescriptor(ExprTypeDescriptor underlyingType)
        : base(
            (underlyingType ?? throw new ArgumentNullException(nameof(underlyingType))).Kind,
            $"{underlyingType.DisplayName}?")
    {
        if (underlyingType.Kind is ExprTypeKind.Nil || underlyingType is NullableTypeDescriptor)
        {
            throw new ArgumentException("A nullable type requires a non-null, non-nullable underlying type.", nameof(underlyingType));
        }

        UnderlyingType = underlyingType;
    }

    /// <summary>Gets the non-null semantic type.</summary>
    public ExprTypeDescriptor UnderlyingType { get; }

    /// <inheritdoc />
    public override bool IsEquivalentTo(ExprTypeDescriptor other) =>
        other.Kind is ExprTypeKind.Any ||
        (other is NullableTypeDescriptor nullable
            ? UnderlyingType.IsEquivalentTo(nullable.UnderlyingType)
            : UnderlyingType.IsEquivalentTo(other));
}

/// <summary>Describes an ordered collection and its element type.</summary>
public sealed record ArrayTypeDescriptor : ExprTypeDescriptor
{
    /// <summary>
    /// Initializes an array descriptor.
    /// </summary>
    /// <param name="elementType">The element type.</param>
    public ArrayTypeDescriptor(ExprTypeDescriptor elementType)
        : base(ExprTypeKind.Array, $"Array{{{elementType}}}")
    {
        ElementType = elementType;
    }

    /// <summary>Gets the collection element type.</summary>
    public ExprTypeDescriptor ElementType { get; }

    /// <inheritdoc />
    public override bool IsEquivalentTo(ExprTypeDescriptor other) =>
        other.Kind is ExprTypeKind.Any ||
        other is ArrayTypeDescriptor array && ElementType.IsEquivalentTo(array.ElementType);
}

/// <summary>Describes a map with optional statically known string keys.</summary>
public sealed record MapTypeDescriptor : ExprTypeDescriptor
{
    /// <summary>
    /// Initializes a map descriptor.
    /// </summary>
    /// <param name="fields">The statically known fields.</param>
    /// <param name="additionalValueType">The type accepted for unknown keys, or <see langword="null"/> for a strict map.</param>
    /// <param name="keyType">The key type. Known-field maps default to strings.</param>
    public MapTypeDescriptor(
        IEnumerable<KeyValuePair<string, ExprTypeDescriptor>> fields,
        ExprTypeDescriptor? additionalValueType = null,
        ExprTypeDescriptor? keyType = null)
        : base(ExprTypeKind.Map, FormatName(fields, additionalValueType, out IReadOnlyDictionary<string, ExprTypeDescriptor> snapshot))
    {
        Fields = snapshot;
        AdditionalValueType = additionalValueType;
        KeyType = keyType ?? ExprTypes.String;
    }

    /// <summary>Gets the statically known fields.</summary>
    public IReadOnlyDictionary<string, ExprTypeDescriptor> Fields { get; }

    /// <summary>Gets the map key type.</summary>
    public ExprTypeDescriptor KeyType { get; }

    /// <summary>Gets the value type for unknown keys, or <see langword="null"/> when the map is strict.</summary>
    public ExprTypeDescriptor? AdditionalValueType { get; }

    /// <summary>Gets a value indicating whether unknown keys are rejected.</summary>
    public bool IsStrict => AdditionalValueType is null;

    /// <summary>Resolves the type of a key.</summary>
    /// <param name="name">The map key.</param>
    /// <param name="type">The resolved type.</param>
    /// <returns><see langword="true"/> when the key is accepted.</returns>
    public bool TryGetField(string name, out ExprTypeDescriptor? type)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Fields.TryGetValue(name, out type))
        {
            return true;
        }

        if (!KeyType.IsEquivalentTo(ExprTypes.String))
        {
            type = null;
            return false;
        }

        type = AdditionalValueType;
        return type is not null;
    }

    /// <inheritdoc />
    public override bool IsEquivalentTo(ExprTypeDescriptor other)
    {
        if (other.Kind is ExprTypeKind.Any)
        {
            return true;
        }

        if (other is not MapTypeDescriptor map ||
            !KeyType.IsEquivalentTo(map.KeyType) ||
            Fields.Count != map.Fields.Count)
        {
            return false;
        }

        if (AdditionalValueType is null && map.AdditionalValueType is not null ||
            AdditionalValueType is not null && map.AdditionalValueType is null)
        {
            return false;
        }

        if (AdditionalValueType is not null &&
            map.AdditionalValueType is not null &&
            !AdditionalValueType.IsEquivalentTo(map.AdditionalValueType))
        {
            return false;
        }

        foreach ((string name, ExprTypeDescriptor type) in Fields)
        {
            if (!map.Fields.TryGetValue(name, out ExprTypeDescriptor? otherType) || !type.IsEquivalentTo(otherType))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatName(
        IEnumerable<KeyValuePair<string, ExprTypeDescriptor>> fields,
        ExprTypeDescriptor? additionalValueType,
        out IReadOnlyDictionary<string, ExprTypeDescriptor> snapshot)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var copy = new Dictionary<string, ExprTypeDescriptor>(StringComparer.Ordinal);
        foreach ((string key, ExprTypeDescriptor value) in fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            copy.Add(key, value);
        }

        snapshot = new ReadOnlyDictionary<string, ExprTypeDescriptor>(copy);
        IEnumerable<string> pairs = copy.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}: {pair.Value}");
        if (additionalValueType is not null)
        {
            pairs = pairs.Append($"*: {additionalValueType}");
        }

        return $"Map{{{string.Join(", ", pairs)}}}";
    }
}

/// <summary>Describes a CLR object type.</summary>
public sealed record ObjectTypeDescriptor : ExprTypeDescriptor
{
    /// <summary>
    /// Initializes an object descriptor.
    /// </summary>
    /// <param name="clrType">The represented CLR type.</param>
    public ObjectTypeDescriptor(Type clrType)
        : base(ExprTypeKind.Object, (clrType ?? throw new ArgumentNullException(nameof(clrType))).FullName ?? clrType.Name)
    {
        ClrType = clrType;
    }

    /// <summary>Gets the represented CLR type.</summary>
    public Type ClrType { get; }
}

/// <summary>Describes a function signature.</summary>
public sealed record FunctionTypeDescriptor : ExprTypeDescriptor
{
    /// <summary>
    /// Initializes a function type descriptor.
    /// </summary>
    /// <param name="parameterTypes">The ordered parameter types.</param>
    /// <param name="returnType">The return type.</param>
    /// <param name="isVariadic">Whether the final parameter can be repeated.</param>
    public FunctionTypeDescriptor(
        IEnumerable<ExprTypeDescriptor> parameterTypes,
        ExprTypeDescriptor returnType,
        bool isVariadic = false)
        : base(ExprTypeKind.Function, "function")
    {
        ArgumentNullException.ThrowIfNull(parameterTypes);
        Parameters = Array.AsReadOnly(parameterTypes.ToArray());
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        IsVariadic = isVariadic;
        if (isVariadic && Parameters.Count is 0)
        {
            throw new ArgumentException("A variadic function requires at least one parameter.", nameof(parameterTypes));
        }
    }

    /// <summary>Gets the ordered parameter types.</summary>
    public IReadOnlyList<ExprTypeDescriptor> Parameters { get; }

    /// <summary>Gets the return type.</summary>
    public ExprTypeDescriptor ReturnType { get; }

    /// <summary>Gets a value indicating whether the final parameter can be repeated.</summary>
    public bool IsVariadic { get; }
}
