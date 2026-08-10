using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Expr.Runtime;
using Expr.Types;

namespace Expr.Checking;

internal sealed class ClrTypeModel
{
    private static readonly ConcurrentDictionary<Type, ClrTypeModel> Cache = new();

    private ClrTypeModel(
        IReadOnlyDictionary<string, ClrValueMember> members,
        IReadOnlyDictionary<string, IReadOnlyList<MethodInfo>> methods,
        ExprTypeDescriptor? valueProviderType)
    {
        Members = members;
        Methods = methods;
        ValueProviderType = valueProviderType;
    }

    internal IReadOnlyDictionary<string, ClrValueMember> Members { get; }

    internal IReadOnlyDictionary<string, IReadOnlyList<MethodInfo>> Methods { get; }

    internal ExprTypeDescriptor? ValueProviderType { get; }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Reflection-backed checking is documented on ExprChecker; AOT callers should use primitive, map, and explicitly declared function metadata.")]
    internal static ClrTypeModel Get(Type type) => Cache.GetOrAdd(type, static clrType => Create(clrType));

    private static ClrTypeModel Create(Type type)
    {
        var members = new Dictionary<string, ClrValueMember>(StringComparer.Ordinal);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length is not 0)
            {
                continue;
            }

            ExprMemberAttribute? attribute = property.GetCustomAttribute<ExprMemberAttribute>(inherit: true);
            if (attribute?.Ignore is true)
            {
                continue;
            }

            string name = attribute?.Name ?? property.Name;
            members.TryAdd(name, new ClrValueMember(property, ExprTypes.FromClrType(property.PropertyType)));
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            ExprMemberAttribute? attribute = field.GetCustomAttribute<ExprMemberAttribute>(inherit: true);
            if (attribute?.Ignore is true)
            {
                continue;
            }

            string name = attribute?.Name ?? field.Name;
            members.TryAdd(name, new ClrValueMember(field, ExprTypes.FromClrType(field.FieldType)));
        }

        var methods = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method =>
                !method.IsSpecialName &&
                !method.ContainsGenericParameters &&
                !method.ReturnType.IsByRef &&
                method.GetParameters().All(static parameter =>
                    !parameter.IsOut && !parameter.ParameterType.IsByRef && !parameter.ParameterType.IsPointer) &&
                method.DeclaringType != typeof(object))
            .GroupBy(static method => method.Name, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<MethodInfo>)Array.AsReadOnly(group.ToArray()),
                StringComparer.Ordinal);
        if (type == typeof(TimeZoneInfo))
        {
            MethodInfo stringMethod = type.GetProperty(nameof(TimeZoneInfo.Id))?.GetMethod ??
                throw new InvalidOperationException("TimeZoneInfo.Id is unavailable.");
            methods["String"] = Array.AsReadOnly([stringMethod]);
        }

        Type? typedProvider = type.GetInterfaces().Append(type).FirstOrDefault(static candidate =>
            candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == typeof(Patching.IExprValueProvider<>));
        ExprTypeDescriptor? valueProviderType = typedProvider is not null
            ? ExprTypes.FromClrType(typedProvider.GetGenericArguments()[0])
            : typeof(Patching.IExprValueProvider).IsAssignableFrom(type)
                ? ExprTypes.Any
                : null;
        return new ClrTypeModel(members, methods, valueProviderType);
    }
}

internal sealed record ClrValueMember(MemberInfo Member, ExprTypeDescriptor Type);
