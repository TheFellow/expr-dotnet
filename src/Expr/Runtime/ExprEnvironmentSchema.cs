using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Expr.Types;

namespace Expr.Runtime;

/// <summary>Gets a named value from an environment instance.</summary>
/// <param name="environment">The environment instance.</param>
/// <returns>The member value.</returns>
public delegate object? ExprMemberAccessor(object environment);

/// <summary>Describes one expression-visible environment member.</summary>
public sealed record ExprEnvironmentMember
{
    /// <summary>
    /// Initializes an environment member.
    /// </summary>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="type">The member type.</param>
    /// <param name="accessor">The cached accessor.</param>
    public ExprEnvironmentMember(string name, ExprTypeDescriptor type, ExprMemberAccessor accessor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    /// <summary>Gets the expression-visible name.</summary>
    public string Name { get; }

    /// <summary>Gets the static member type.</summary>
    public ExprTypeDescriptor Type { get; }

    /// <summary>Gets the accessor prepared during schema construction.</summary>
    public ExprMemberAccessor Accessor { get; }
}

/// <summary>
/// Contains immutable type and access information for an Expr environment.
/// </summary>
public sealed class ExprEnvironmentSchema
{
    private static readonly ConcurrentDictionary<Type, ExprEnvironmentSchema> ReflectionCache = new();
    private static readonly MethodInfo CreatePropertyAccessorMethod = typeof(ExprEnvironmentSchema)
        .GetMethod(nameof(CreatePropertyAccessorCore), BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException("The property accessor factory is unavailable.");

    private readonly IReadOnlyDictionary<string, ExprEnvironmentMember> members;

    internal ExprEnvironmentSchema(Type environmentType, IEnumerable<ExprEnvironmentMember> members, bool isStrict)
    {
        EnvironmentType = environmentType;
        IsStrict = isStrict;
        var copy = new Dictionary<string, ExprEnvironmentMember>(StringComparer.Ordinal);
        foreach (ExprEnvironmentMember member in members)
        {
            copy.Add(member.Name, member);
        }

        this.members = new ReadOnlyDictionary<string, ExprEnvironmentMember>(copy);
    }

    /// <summary>Gets the CLR environment type.</summary>
    public Type EnvironmentType { get; }

    /// <summary>Gets the expression-visible members.</summary>
    public IReadOnlyDictionary<string, ExprEnvironmentMember> Members => members;

    /// <summary>Gets a value indicating whether unknown names are rejected.</summary>
    public bool IsStrict { get; }

    /// <summary>
    /// Reflects a CLR environment once and caches the resulting schema and accessors.
    /// </summary>
    /// <remarks>
    /// Native AOT applications should use <see cref="ExprEnvironmentSchemaBuilder{TEnvironment}"/>
    /// so member access is fully static and requires no preserved reflection metadata.
    /// </remarks>
    /// <typeparam name="TEnvironment">The CLR environment type.</typeparam>
    /// <returns>The cached schema.</returns>
    [RequiresDynamicCode("Reflection-based schemas close generic accessor methods at runtime. Use ExprEnvironmentSchemaBuilder<TEnvironment> for Native AOT.")]
    [RequiresUnreferencedCode("Public environment properties must be preserved. Use ExprEnvironmentSchemaBuilder<TEnvironment> for trimming and Native AOT.")]
    public static ExprEnvironmentSchema Reflect<TEnvironment>() => Reflect(typeof(TEnvironment));

    /// <summary>
    /// Reflects a CLR environment once and caches the resulting schema and accessors.
    /// </summary>
    /// <param name="environmentType">The CLR environment type.</param>
    /// <returns>The cached schema.</returns>
    [RequiresDynamicCode("Reflection-based schemas close generic accessor methods at runtime. Use ExprEnvironmentSchemaBuilder<TEnvironment> for Native AOT.")]
    [RequiresUnreferencedCode("Public environment properties must be preserved. Use ExprEnvironmentSchemaBuilder<TEnvironment> for trimming and Native AOT.")]
    public static ExprEnvironmentSchema Reflect(Type environmentType)
    {
        ArgumentNullException.ThrowIfNull(environmentType);
        return ReflectionCache.GetOrAdd(environmentType, static type => CreateReflected(type));
    }

    /// <summary>Creates a strict schema over a string-keyed environment dictionary.</summary>
    /// <param name="environment">The environment whose current keys define the schema.</param>
    /// <returns>The schema.</returns>
    [RequiresUnreferencedCode("Dictionary value types are inferred from runtime CLR metadata. Build an explicitly typed schema for trimming and Native AOT.")]
    public static ExprEnvironmentSchema FromDictionary(IReadOnlyDictionary<string, object?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ExprEnvironmentMember[] dictionaryMembers = [.. environment.Select(pair =>
            new ExprEnvironmentMember(
                pair.Key,
                pair.Value is null ? ExprTypes.Nil : ExprTypes.FromClrType(pair.Value.GetType()),
                instance => ((IReadOnlyDictionary<string, object?>)instance)[pair.Key]))];
        return new ExprEnvironmentSchema(environment.GetType(), dictionaryMembers, true);
    }

    /// <summary>Attempts to resolve a member by its ordinal, case-sensitive name.</summary>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="member">The resolved member.</param>
    /// <returns><see langword="true"/> when the member exists.</returns>
    public bool TryGetMember(string name, out ExprEnvironmentMember? member)
    {
        ArgumentNullException.ThrowIfNull(name);
        return members.TryGetValue(name, out member);
    }

    /// <summary>Reads a named member from an environment without performing reflection lookup.</summary>
    /// <param name="environment">The environment instance.</param>
    /// <param name="name">The expression-visible name.</param>
    /// <returns>The member value.</returns>
    /// <exception cref="ExprRuntimeException">The name is unknown or the environment type is invalid.</exception>
    public object? Read(object environment, string name)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(name);
        if (!EnvironmentType.IsInstanceOfType(environment))
        {
            throw new ExprRuntimeException(
                $"environment type {environment.GetType().FullName} is not assignable to {EnvironmentType.FullName}");
        }

        if (!members.TryGetValue(name, out ExprEnvironmentMember? member))
        {
            throw new ExprRuntimeException($"unknown name {name}");
        }

        return member.Accessor(environment);
    }

    [RequiresDynamicCode("Creates a closed generic accessor at runtime.")]
    [RequiresUnreferencedCode("The reflected property getter must be preserved.")]
    private static ExprEnvironmentSchema CreateReflected(Type environmentType)
    {
        if (!environmentType.IsClass)
        {
            throw new ArgumentException(
                "Reflection-based environment schemas require a reference type. Use ExprEnvironmentSchemaBuilder<TEnvironment> for value types.",
                nameof(environmentType));
        }

        IEnumerable<ExprEnvironmentMember?> reflectedProperties = environmentType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetMethod is not null && property.GetIndexParameters().Length is 0)
            .Select(CreateMember);
        IEnumerable<ExprEnvironmentMember?> reflectedFields = environmentType
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(CreateFieldMember);
        IEnumerable<ExprEnvironmentMember> reflectedMembers = reflectedProperties
            .Concat(reflectedFields)
            .OfType<ExprEnvironmentMember>();
        return new ExprEnvironmentSchema(environmentType, reflectedMembers, true);
    }

    [RequiresDynamicCode("Creates a closed generic accessor at runtime.")]
    [RequiresUnreferencedCode("The reflected property getter must be preserved.")]
    private static ExprEnvironmentMember? CreateMember(PropertyInfo property)
    {
        ExprMemberAttribute? attribute = property.GetCustomAttribute<ExprMemberAttribute>(inherit: true);
        if (attribute?.Ignore is true)
        {
            return null;
        }

        string name = attribute?.Name ?? property.Name;
        MethodInfo factory = CreatePropertyAccessorMethod.MakeGenericMethod(property.DeclaringType!, property.PropertyType);
        var accessor = factory.Invoke(null, [property]) as ExprMemberAccessor ??
            throw new InvalidOperationException($"An accessor for {property.Name} could not be created.");
        return new ExprEnvironmentMember(name, ExprTypes.FromClrType(property.PropertyType), accessor);
    }

    [RequiresUnreferencedCode("The reflected field must be preserved.")]
    private static ExprEnvironmentMember? CreateFieldMember(FieldInfo field)
    {
        ExprMemberAttribute? attribute = field.GetCustomAttribute<ExprMemberAttribute>(inherit: true);
        if (attribute?.Ignore is true)
        {
            return null;
        }

        string name = attribute?.Name ?? field.Name;
        return new ExprEnvironmentMember(name, ExprTypes.FromClrType(field.FieldType), field.GetValue);
    }

    private static ExprMemberAccessor CreatePropertyAccessorCore<TEnvironment, TValue>(PropertyInfo property)
        where TEnvironment : class
    {
        MethodInfo getterMethod = property.GetMethod ??
            throw new InvalidOperationException($"Property {property.Name} has no getter.");
        Func<TEnvironment, TValue> getter = getterMethod.CreateDelegate<Func<TEnvironment, TValue>>();
        return environment => getter((TEnvironment)environment);
    }
}

/// <summary>
/// Builds a reflection-free, Native-AOT-safe environment schema from strongly typed accessors.
/// </summary>
/// <typeparam name="TEnvironment">The environment type.</typeparam>
public sealed class ExprEnvironmentSchemaBuilder<TEnvironment>
{
    private readonly List<ExprEnvironmentMember> members = [];

    /// <summary>Adds a named environment member.</summary>
    /// <typeparam name="TValue">The member value type.</typeparam>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="accessor">The strongly typed accessor.</param>
    /// <param name="type">The semantic type exposed to Expr.</param>
    /// <returns>This builder.</returns>
    public ExprEnvironmentSchemaBuilder<TEnvironment> Member<TValue>(
        string name,
        Func<TEnvironment, TValue> accessor,
        ExprTypeDescriptor type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(type);
        members.Add(new ExprEnvironmentMember(
            name,
            type,
            environment => accessor((TEnvironment)environment)));
        return this;
    }

    /// <summary>Adds a named environment member and discovers its semantic type from CLR metadata.</summary>
    /// <remarks>
    /// Native AOT and trimmed applications should call the overload that accepts an explicit
    /// <see cref="ExprTypeDescriptor"/>. That overload does not inspect <typeparamref name="TValue"/>.
    /// </remarks>
    /// <typeparam name="TValue">The member value type.</typeparam>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="accessor">The strongly typed accessor.</param>
    /// <returns>This builder.</returns>
    [RequiresUnreferencedCode("Inferring an Expr type inspects CLR collection and delegate metadata. Pass an explicit ExprTypeDescriptor for trimming and Native AOT.")]
    public ExprEnvironmentSchemaBuilder<TEnvironment> Member<TValue>(
        string name,
        Func<TEnvironment, TValue> accessor) =>
        Member(name, accessor, ExprTypes.FromClrType<TValue>());

    /// <summary>Adds a generic read-only list member through a reflection-free Expr array adapter.</summary>
    /// <typeparam name="TValue">The declared element type.</typeparam>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="accessor">The strongly typed list accessor.</param>
    /// <param name="elementType">The semantic element type.</param>
    /// <returns>This builder.</returns>
    public ExprEnvironmentSchemaBuilder<TEnvironment> ArrayMember<TValue>(
        string name,
        Func<TEnvironment, IReadOnlyList<TValue>> accessor,
        ExprTypeDescriptor elementType)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(elementType);
        return Member(
            name,
            environment => ExprCollections.AsArray(accessor(environment)),
            ExprTypes.ArrayOf(elementType));
    }

    /// <summary>Adds a generic read-only dictionary member through a reflection-free Expr map adapter.</summary>
    /// <typeparam name="TKey">The declared key type.</typeparam>
    /// <typeparam name="TValue">The declared value type.</typeparam>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="accessor">The strongly typed dictionary accessor.</param>
    /// <param name="keyType">The semantic key type.</param>
    /// <param name="valueType">The semantic value type.</param>
    /// <returns>This builder.</returns>
    public ExprEnvironmentSchemaBuilder<TEnvironment> MapMember<TKey, TValue>(
        string name,
        Func<TEnvironment, IReadOnlyDictionary<TKey, TValue>> accessor,
        ExprTypeDescriptor keyType,
        ExprTypeDescriptor valueType)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentNullException.ThrowIfNull(valueType);
        return Member(
            name,
            environment => ExprCollections.AsMap(accessor(environment)),
            new MapTypeDescriptor([], valueType, keyType));
    }

    /// <summary>Creates the immutable strict schema.</summary>
    /// <returns>The completed schema.</returns>
    public ExprEnvironmentSchema Build() => new(typeof(TEnvironment), members, true);
}
