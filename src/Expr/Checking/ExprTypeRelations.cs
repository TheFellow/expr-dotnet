using System;
using System.Collections.Generic;
using Expr.Types;

namespace Expr.Checking;

internal static class ExprTypeRelations
{
    internal static bool IsUnknown(ExprTypeDescriptor type) =>
        type.Kind is ExprTypeKind.Any or ExprTypeKind.Unknown;

    internal static bool IsNumber(ExprTypeDescriptor type) =>
        type.Kind is ExprTypeKind.Integer or ExprTypeKind.Float;

    internal static bool IsCollection(ExprTypeDescriptor type) =>
        type.Kind is ExprTypeKind.Array or ExprTypeKind.Map;

    internal static bool CanAssign(ExprTypeDescriptor value, ExprTypeDescriptor target)
    {
        if (IsUnknown(value) || IsUnknown(target))
        {
            return true;
        }

        if (target is NullableTypeDescriptor nullableTarget)
        {
            return value.Kind is ExprTypeKind.Nil || CanAssign(Unwrap(value), nullableTarget.UnderlyingType);
        }

        if (value is NullableTypeDescriptor nullableValue)
        {
            return CanAssign(nullableValue.UnderlyingType, Unwrap(target));
        }

        if (value.Kind is ExprTypeKind.Nil)
        {
            return target.Kind is ExprTypeKind.Nil or ExprTypeKind.Any or ExprTypeKind.Array or
                ExprTypeKind.Map or ExprTypeKind.Object or ExprTypeKind.Function;
        }

        if (value.Kind is ExprTypeKind.Integer && target.Kind is ExprTypeKind.Float)
        {
            return true;
        }

        if (value is ObjectTypeDescriptor valueObject && target is ObjectTypeDescriptor targetObject)
        {
            return targetObject.ClrType.IsAssignableFrom(valueObject.ClrType);
        }

        if (value is ArrayTypeDescriptor valueArray && target is ArrayTypeDescriptor targetArray)
        {
            return CanAssign(valueArray.ElementType, targetArray.ElementType);
        }

        if (value is MapTypeDescriptor valueMap && target is MapTypeDescriptor targetMap)
        {
            return CanAssign(valueMap.KeyType, targetMap.KeyType) &&
                MapValuesCompatible(valueMap, targetMap);
        }

        return value.Kind == target.Kind;
    }

    internal static bool Comparable(ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (IsUnknown(left) || IsUnknown(right))
        {
            return true;
        }

        if (left.Kind is ExprTypeKind.Nil || right.Kind is ExprTypeKind.Nil)
        {
            // Expr permits equality checks against nil for every type. The comparison
            // remains a bool even when nil could not be assigned to the other operand.
            return true;
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return true;
        }

        if (left.Kind is ExprTypeKind.Array && right.Kind is ExprTypeKind.Array)
        {
            return true;
        }

        return CanAssign(left, right) || CanAssign(right, left);
    }

    internal static ExprTypeDescriptor PromoteNumber(ExprTypeDescriptor left, ExprTypeDescriptor right) =>
        left.Kind is ExprTypeKind.Float || right.Kind is ExprTypeKind.Float
            ? ExprTypes.Float
            : ExprTypes.Integer;

    internal static ExprTypeDescriptor CommonType(ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (left.Kind is ExprTypeKind.Nil && right.Kind is not ExprTypeKind.Nil)
        {
            return right;
        }

        if (right.Kind is ExprTypeKind.Nil && left.Kind is not ExprTypeKind.Nil)
        {
            return left;
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return PromoteNumber(left, right);
        }

        if (left is ArrayTypeDescriptor leftArray && right is ArrayTypeDescriptor rightArray)
        {
            return new ArrayTypeDescriptor(CommonType(leftArray.ElementType, rightArray.ElementType));
        }

        if (CanAssign(left, right))
        {
            return right.Kind is ExprTypeKind.Any ? left : right;
        }

        if (CanAssign(right, left))
        {
            return left;
        }

        return ExprTypes.Any;
    }

    internal static int MatchScore(ExprTypeDescriptor value, ExprTypeDescriptor target)
    {
        if (target.Kind is ExprTypeKind.Any)
        {
            return 1;
        }

        if (value.Equals(target))
        {
            return 8;
        }

        if (value.Kind == target.Kind)
        {
            return 6;
        }

        if (value.Kind is ExprTypeKind.Integer && target.Kind is ExprTypeKind.Float)
        {
            return 4;
        }

        return CanAssign(value, target) ? 2 : -1;
    }

    private static ExprTypeDescriptor Unwrap(ExprTypeDescriptor type) =>
        type is NullableTypeDescriptor nullable ? nullable.UnderlyingType : type;

    private static bool MapValuesCompatible(MapTypeDescriptor value, MapTypeDescriptor target)
    {
        if (target.AdditionalValueType is not null)
        {
            if (value.AdditionalValueType is not null &&
                !CanAssign(value.AdditionalValueType, target.AdditionalValueType))
            {
                return false;
            }

            foreach (ExprTypeDescriptor field in value.Fields.Values)
            {
                if (!CanAssign(field, target.AdditionalValueType))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (KeyValuePair<string, ExprTypeDescriptor> field in target.Fields)
        {
            if (value.Fields.TryGetValue(field.Key, out ExprTypeDescriptor? valueType))
            {
                if (!CanAssign(valueType, field.Value))
                {
                    return false;
                }

                continue;
            }

            if (value.AdditionalValueType is null ||
                !CanAssign(value.AdditionalValueType, field.Value))
            {
                return false;
            }
        }

        return true;
    }
}
