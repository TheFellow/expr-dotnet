using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Expr.Types;

/// <summary>
/// Provides canonical Expr type descriptors and CLR-to-Expr type mapping.
/// </summary>
public static class ExprTypes
{
    /// <summary>Gets the unknown type.</summary>
    public static PrimitiveTypeDescriptor Unknown { get; } = new(ExprTypeKind.Unknown, "unknown");

    /// <summary>Gets the nil type.</summary>
    public static PrimitiveTypeDescriptor Nil { get; } = new(ExprTypeKind.Nil, "nil");

    /// <summary>Gets the wildcard type.</summary>
    public static PrimitiveTypeDescriptor Any { get; } = new(ExprTypeKind.Any, "any", typeof(object));

    /// <summary>Gets the Boolean type.</summary>
    public static PrimitiveTypeDescriptor Boolean { get; } = new(ExprTypeKind.Boolean, "bool", typeof(bool));

    /// <summary>Gets the canonical integer type.</summary>
    public static PrimitiveTypeDescriptor Integer { get; } = new(ExprTypeKind.Integer, "int", typeof(long));

    /// <summary>Gets the canonical floating-point type.</summary>
    public static PrimitiveTypeDescriptor Float { get; } = new(ExprTypeKind.Float, "float64", typeof(double));

    /// <summary>Gets the string type.</summary>
    public static PrimitiveTypeDescriptor String { get; } = new(ExprTypeKind.String, "string", typeof(string));

    /// <summary>Gets the instant-in-time type.</summary>
    public static PrimitiveTypeDescriptor Time { get; } = new(ExprTypeKind.Time, "time.Time", typeof(DateTimeOffset));

    /// <summary>Gets the duration type.</summary>
    public static PrimitiveTypeDescriptor Duration { get; } = new(ExprTypeKind.Duration, "time.Duration", typeof(TimeSpan));

    /// <summary>Creates an array type.</summary>
    /// <param name="elementType">The array element type.</param>
    /// <returns>The array descriptor.</returns>
    public static ArrayTypeDescriptor ArrayOf(ExprTypeDescriptor elementType) => new(elementType);

    /// <summary>Creates a strict map type.</summary>
    /// <param name="fields">The statically known fields.</param>
    /// <returns>The map descriptor.</returns>
    public static MapTypeDescriptor MapOf(IEnumerable<KeyValuePair<string, ExprTypeDescriptor>> fields) => new(fields);

    /// <summary>Gets the Expr descriptor for a CLR type.</summary>
    /// <typeparam name="T">The CLR type to describe.</typeparam>
    /// <returns>The semantic descriptor.</returns>
    [RequiresUnreferencedCode("Mapping arbitrary CLR types inspects collection and delegate metadata. Use explicit Expr type descriptors for trimming and Native AOT.")]
    public static ExprTypeDescriptor FromClrType<T>() => FromClrType(typeof(T));

    /// <summary>Gets the Expr descriptor for a CLR type.</summary>
    /// <param name="type">The CLR type to describe.</param>
    /// <returns>The semantic descriptor.</returns>
    [RequiresUnreferencedCode("Mapping arbitrary CLR types inspects collection and delegate metadata. Use explicit Expr type descriptors for trimming and Native AOT.")]
    public static ExprTypeDescriptor FromClrType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(object))
        {
            return Any;
        }

        if (underlying == typeof(bool))
        {
            return Boolean;
        }

        if (underlying == typeof(string))
        {
            return String;
        }

        if (IsIntegral(underlying))
        {
            return Integer;
        }

        if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(Half))
        {
            return Float;
        }

        if (underlying == typeof(DateTimeOffset) || underlying == typeof(DateTime))
        {
            return Time;
        }

        if (underlying == typeof(TimeSpan))
        {
            return Duration;
        }

        if (TryGetDictionaryTypes(underlying, out Type keyType, out Type valueType))
        {
            return new MapTypeDescriptor([], FromClrType(valueType), FromClrType(keyType));
        }

        Type? elementType = GetSequenceElementType(underlying);
        if (elementType is not null)
        {
            return ArrayOf(FromClrType(elementType));
        }

        if (typeof(Delegate).IsAssignableFrom(underlying))
        {
            return FromDelegateType(underlying);
        }

        return new ObjectTypeDescriptor(underlying);
    }

    /// <summary>Determines whether a CLR type belongs to Expr's integer family.</summary>
    /// <param name="type">The CLR type.</param>
    /// <returns><see langword="true"/> for supported integral types.</returns>
    public static bool IsIntegral(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(nint) || underlying == typeof(nuint))
        {
            return true;
        }

        TypeCode code = Type.GetTypeCode(underlying);
        return code is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
    }

    private static ExprTypeDescriptor FromDelegateType(Type type)
    {
        System.Reflection.MethodInfo invoke = type.GetMethod("Invoke") ??
            throw new ArgumentException("The delegate type has no Invoke method.", nameof(type));
        ExprTypeDescriptor[] parameters = invoke.GetParameters()
            .Select(static parameter => FromClrType(parameter.ParameterType))
            .ToArray();
        ExprTypeDescriptor result = invoke.ReturnType == typeof(void) ? Nil : FromClrType(invoke.ReturnType);
        return new FunctionTypeDescriptor(parameters, result);
    }

    private static Type? GetSequenceElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        Type? sequence = type.GetInterfaces().Append(type)
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
        return sequence?.GetGenericArguments()[0];
    }

    private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType)
    {
        Type? dictionary = type.GetInterfaces().Append(type)
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType &&
                (candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) ||
                 candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)));
        if (dictionary is null)
        {
            keyType = typeof(void);
            valueType = typeof(void);
            return false;
        }

        Type[] arguments = dictionary.GetGenericArguments();
        keyType = arguments[0];
        valueType = arguments[1];
        return true;
    }
}
