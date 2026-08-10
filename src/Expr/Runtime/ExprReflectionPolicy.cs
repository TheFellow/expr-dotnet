using System;
using System.Reflection;

namespace Expr.Runtime;

internal static class ExprReflectionPolicy
{
    internal static bool IsForbiddenType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return typeof(Type).IsAssignableFrom(type) ||
            typeof(Assembly).IsAssignableFrom(type) ||
            typeof(Module).IsAssignableFrom(type) ||
            typeof(MemberInfo).IsAssignableFrom(type) ||
            typeof(ParameterInfo).IsAssignableFrom(type) ||
            type.Namespace?.StartsWith("System.Reflection", StringComparison.Ordinal) is true;
    }
}
