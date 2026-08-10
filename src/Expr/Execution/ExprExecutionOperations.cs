using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Expr.Checking;
using Expr.Compilation;
using Expr.Patching;
using Expr.Runtime;

namespace Expr.Execution;

internal static class ExprExecutionOperations
{
    private static readonly ConcurrentDictionary<MethodInfo, MethodInvoker> MethodInvokers = new();

    internal static object? FetchEnvironment(object? environment, string name)
    {
        if (environment is null)
        {
            throw Error($"cannot fetch {name} from nil");
        }

        if (environment is IReadOnlyDictionary<string, object?> readOnly &&
            readOnly.TryGetValue(name, out object? readOnlyValue))
        {
            return readOnlyValue;
        }

        if (environment is IDictionary<string, object?> dictionary &&
            dictionary.TryGetValue(name, out object? value))
        {
            return value;
        }

        if (ExprCollections.TryAsMap(environment, out IExprMap? map) && map is not null)
        {
            return map.TryGetValue(name, out object? mapped) ? mapped : null;
        }

        throw Error($"cannot fetch {name} from {TypeName(environment)}");
    }

    internal static object? FetchBound(object? target, ExprMemberOperand operand)
    {
        if (target is null)
        {
            throw Error($"cannot get {operand.Name} from nil");
        }

        if (operand.EnvironmentMember is ExprEnvironmentMember environmentMember)
        {
            return environmentMember.Accessor(target);
        }

        return operand.Kind switch
        {
            ExprMemberBindingKind.ClrMethod when operand.Member is MethodInfo method =>
                new BoundMethod(target, method),
            ExprMemberBindingKind.ClrMember when operand.Member is not null =>
                ReadMember(target, operand.Member),
            ExprMemberBindingKind.Environment => FetchEnvironment(target, operand.Name),
            ExprMemberBindingKind.Index => Fetch(target, operand.Name),
            _ => throw Error($"cannot get {operand.Name} from {TypeName(target)}"),
        };
    }

    internal static object? Fetch(object? target, object? key)
    {
        if (target is null)
        {
            throw Error($"cannot fetch {Display(key)} from nil");
        }

        if (TryGetBytes(target, out ReadOnlyMemory<byte> bytes))
        {
            if (key is string)
            {
                throw Error($"cannot fetch {Display(key)} from {TypeName(target)}");
            }

            long requested = ExprValue.ToInt64(key);
            long index = requested < 0 ? bytes.Length + requested : requested;
            if (index < 0 || index >= bytes.Length)
            {
                throw Error($"index out of range: {index} (array length is {bytes.Length})");
            }

            return bytes.Span[(int)index];
        }

        if (ExprCollections.TryAsArray(target, out IExprArray? array) && array is not null)
        {
            if (key is string)
            {
                throw Error($"cannot fetch {Display(key)} from {TypeName(target)}");
            }

            return ExprValue.FetchIndex(array, ExprValue.ToInt64(key));
        }

        if (target is string)
        {
            if (key is string)
            {
                throw Error($"cannot fetch {Display(key)} from {TypeName(target)}");
            }

            return ExprValue.FetchIndex(target, ExprValue.ToInt64(key));
        }

        if (ExprCollections.TryAsMap(target, out IExprMap? map) && map is not null)
        {
            return map.TryGetValue(key, out object? value) ? value : null;
        }

        throw Error($"cannot fetch {Display(key)} from {TypeName(target)}");
    }

    internal static object? Negate(object? value) => value switch
    {
        sbyte number => unchecked((sbyte)-number),
        byte number => unchecked((byte)-number),
        short number => unchecked((short)-number),
        ushort number => unchecked((ushort)-number),
        int number => unchecked(-number),
        uint number => unchecked(0U - number),
        long number => unchecked(-number),
        ulong number => unchecked(0UL - number),
        nint number => unchecked(-number),
        nuint number => unchecked(0U - number),
        Half number => -number,
        float number => -number,
        double number => -number,
        _ => throw Error($"invalid operation: - {TypeName(value)}"),
    };

    internal static object? Add(object? left, object? right)
    {
        if (left is string leftText && right is string rightText)
        {
            return string.Concat(leftText, rightText);
        }

        if (TryGetInstant(left, out DateTimeOffset leftTime) && right is TimeSpan rightDuration)
        {
            return leftTime.Add(rightDuration);
        }

        if (left is TimeSpan leftDuration)
        {
            if (TryGetInstant(right, out DateTimeOffset rightTime))
            {
                return rightTime.Add(leftDuration);
            }

            if (right is TimeSpan otherDuration)
            {
                return leftDuration + otherDuration;
            }
        }

        return Numeric(left, right, static (a, b) => unchecked(a + b), static (a, b) => a + b, "+");
    }

    internal static object? Subtract(object? left, object? right)
    {
        if (TryGetInstant(left, out DateTimeOffset leftTime))
        {
            if (TryGetInstant(right, out DateTimeOffset rightTime))
            {
                return leftTime - rightTime;
            }

            if (right is TimeSpan duration)
            {
                return leftTime.Subtract(duration);
            }
        }

        if (left is TimeSpan leftDuration && right is TimeSpan rightDuration)
        {
            return leftDuration - rightDuration;
        }

        return Numeric(left, right, static (a, b) => unchecked(a - b), static (a, b) => a - b, "-");
    }

    internal static object? Multiply(object? left, object? right)
    {
        if (left is TimeSpan leftDuration && IsIntegral(right))
        {
            return TimeSpan.FromTicks(unchecked(leftDuration.Ticks * ExprValue.ToInt64(right)));
        }

        if (right is TimeSpan rightDuration && IsIntegral(left))
        {
            return TimeSpan.FromTicks(unchecked(rightDuration.Ticks * ExprValue.ToInt64(left)));
        }

        if (left is TimeSpan floatLeft && IsFloating(right))
        {
            return floatLeft.Ticks * ExprValue.ToDouble(right);
        }

        if (right is TimeSpan floatRight && IsFloating(left))
        {
            return floatRight.Ticks * ExprValue.ToDouble(left);
        }

        return Numeric(left, right, static (a, b) => unchecked(a * b), static (a, b) => a * b, "*");
    }

    internal static double Divide(object? left, object? right)
    {
        RequireNumeric(left, right, "/");
        return ExprValue.ToDouble(left) / ExprValue.ToDouble(right);
    }

    internal static long Modulo(object? left, object? right)
    {
        if (!IsIntegral(left) || !IsIntegral(right))
        {
            throw Error($"invalid operation: {TypeName(left)} % {TypeName(right)}");
        }

        long dividend = ExprValue.ToInt64(left);
        long divisor = ExprValue.ToInt64(right);
        if (divisor is 0)
        {
            throw Error("runtime error: integer divide by zero");
        }

        return dividend is long.MinValue && divisor is -1 ? 0 : dividend % divisor;
    }

    internal static object Slice(object? value, object? fromValue, object? toValue)
    {
        long from = ExprValue.ToInt64(fromValue);
        long to = ExprValue.ToInt64(toValue);
        int length = TryGetBytes(value, out ReadOnlyMemory<byte> bytes)
            ? bytes.Length
            : ExprValue.StorageLength(value);
        long start = NormalizeSliceIndex(from, length);
        long end = NormalizeSliceIndex(to, length);
        if (start > end)
        {
            start = end;
        }

        if (TryGetBytes(value, out bytes))
        {
            return new ReadOnlyMemory<byte>(bytes.Slice((int)start, (int)(end - start)).ToArray());
        }

        if (value is string text)
        {
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(text);
            return Encoding.UTF8.GetString(utf8Bytes, (int)start, (int)(end - start));
        }

        if (ExprCollections.TryAsArray(value, out IExprArray? array) && array is not null)
        {
            var result = new object?[end - start];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = array[(int)start + index];
            }

            return new ExprArray(result);
        }

        throw Error($"cannot slice {Display(fromValue)}");
    }

    internal static ulong SliceAllocationCost(object? value, object? fromValue, object? toValue)
    {
        long from = ExprValue.ToInt64(fromValue);
        long to = ExprValue.ToInt64(toValue);
        int length = TryGetBytes(value, out ReadOnlyMemory<byte> bytes)
            ? bytes.Length
            : ExprValue.StorageLength(value);
        long start = NormalizeSliceIndex(from, length);
        long end = NormalizeSliceIndex(to, length);
        if (start > end)
        {
            start = end;
        }

        ulong resultLength = (ulong)(end - start);
        return value is string
            ? checked(resultLength + (ulong)length)
            : resultLength;
    }

    internal static bool DynamicMatch(
        object? input,
        object? pattern,
        ExprEvaluationOptions options)
    {
        if (input is null || pattern is null)
        {
            return false;
        }

        if (pattern is not string patternText)
        {
            throw Error($"invalid regular expression pattern type {TypeName(pattern)}");
        }

        if (patternText.Length > options.MaximumRegularExpressionLength)
        {
            throw Error(
                $"regular expression exceeds configured maximum length of {options.MaximumRegularExpressionLength}");
        }

        string inputText = input switch
        {
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            _ => throw Error($"invalid regular expression input type {TypeName(input)}"),
        };
        try
        {
            return Regex.IsMatch(
                inputText,
                patternText,
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                options.RegularExpressionTimeout);
        }
        catch (ArgumentException exception)
        {
            throw Error(exception.Message, exception);
        }
        catch (NotSupportedException exception)
        {
            throw Error(exception.Message, exception);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw Error("regular expression match timed out", exception);
        }
    }

    internal static object? Cast(object? value, ExprCastKind kind) => kind switch
    {
        ExprCastKind.Integer or ExprCastKind.Integer64 => ExprValue.ToInt64(value),
        ExprCastKind.Float64 => ExprValue.ToDouble(value),
        ExprCastKind.Boolean => ExprValue.ToBoolean(value),
        _ => throw Error("cast operand is invalid"),
    };

    internal static ExprInvocationResult Invoke(object? callable, object?[] arguments)
    {
        try
        {
            return callable switch
            {
                null => throw Error("invalid operation: cannot call nil"),
                ExprFunction function => function.Invoke(arguments),
                BoundMethod method => new ExprInvocationResult(
                    InvokeMethod(method.Target, method.Method, arguments),
                    0),
                Delegate method => new ExprInvocationResult(
                    InvokeDelegate(method, arguments),
                    0),
                _ => throw Error(
                    $"invalid operation: cannot call non-function of type {TypeName(callable)}"),
            };
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    internal static object? InvokeMethod(object? target, MethodInfo method, object?[] arguments)
    {
        object?[] prepared = PrepareMethodArguments(method, arguments);
        MethodInvoker invoker = MethodInvokers.GetOrAdd(method, static value => MethodInvoker.Create(value));
        return invoker.Invoke(target, prepared);
    }

    private static object? InvokeDelegate(Delegate method, object?[] arguments)
    {
        object? result = null;
        foreach (Delegate invocation in method.GetInvocationList())
        {
            object?[] prepared = PrepareDelegateArguments(invocation, arguments);
            MethodInvoker invoker = MethodInvokers.GetOrAdd(
                invocation.Method,
                static value => MethodInvoker.Create(value));
            result = invoker.Invoke(invocation.Target, prepared);
        }

        return result;
    }

    internal static bool IsValidMapKey(object? key) => key is null or string or bool or char or
        sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or
        Half or float or double or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Enum;

    internal static bool TryGetBytes(object? value, out ReadOnlyMemory<byte> bytes)
    {
        switch (value)
        {
            case byte[] array:
                bytes = array;
                return true;
            case ReadOnlyMemory<byte> memory:
                bytes = memory;
                return true;
            case Memory<byte> memory:
                bytes = memory;
                return true;
            default:
                bytes = default;
                return false;
        }
    }

    private static object? ReadMember(object target, MemberInfo member) => member switch
    {
        PropertyInfo property when property.GetMethod is MethodInfo getter => InvokeMethod(target, getter, []),
        FieldInfo field => field.GetValue(target),
        MethodInfo method => new BoundMethod(target, method),
        _ => throw Error($"cannot get {member.Name} from {TypeName(target)}"),
    };

    private static object?[] PrepareDelegateArguments(Delegate method, object?[] arguments)
    {
        ParameterInfo[] parameters = method.Method.GetParameters();
        return PrepareArguments(parameters, arguments, method.Method.Name);
    }

    private static object?[] PrepareMethodArguments(MethodInfo method, object?[] arguments) =>
        PrepareArguments(method.GetParameters(), arguments, method.Name);

    private static object?[] PrepareArguments(
        ParameterInfo[] parameters,
        object?[] arguments,
        string name)
    {
        bool variadic = parameters.LastOrDefault()?.GetCustomAttribute<ParamArrayAttribute>() is not null;
        int minimum = variadic ? parameters.Length - 1 : parameters.Length;
        if (arguments.Length < minimum || (!variadic && arguments.Length != parameters.Length))
        {
            string expectation = variadic ? $"at least {minimum}" : parameters.Length.ToString(CultureInfo.InvariantCulture);
            throw Error(
                $"invalid number of arguments for {name}: expected {expectation}, got {arguments.Length}");
        }

        var prepared = new object?[parameters.Length];
        for (var index = 0; index < minimum; index++)
        {
            prepared[index] = ConvertArgument(arguments[index], parameters[index].ParameterType);
        }

        if (variadic)
        {
            Type elementType = parameters[^1].ParameterType.GetElementType() ??
                throw Error($"variadic parameter for {name} is not an array");
            int count = arguments.Length - minimum;
            Array array = Array.CreateInstance(elementType, count);
            for (var index = 0; index < count; index++)
            {
                array.SetValue(ConvertArgument(arguments[minimum + index], elementType), index);
            }

            prepared[^1] = array;
        }

        return prepared;
    }

    private static object? ConvertArgument(object? value, Type targetType)
    {
        if (value is null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            {
                return Array.CreateInstance(targetType, 1).GetValue(0);
            }

            return null;
        }

        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(value))
        {
            return value;
        }

        if (effectiveType.IsEnum)
        {
            return Enum.ToObject(effectiveType, ExprValue.ToInt64(value));
        }

        if (effectiveType == typeof(DateTimeOffset) && TryGetInstant(value, out DateTimeOffset instant))
        {
            return instant;
        }

        if (effectiveType == typeof(DateTime) && TryGetInstant(value, out instant))
        {
            return instant.UtcDateTime;
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(effectiveType))
        {
            return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
        }

        throw Error($"cannot use {TypeName(value)} as {effectiveType.FullName}");
    }

    private static object Numeric(
        object? left,
        object? right,
        Func<long, long, long> integer,
        Func<double, double, double> floating,
        string operation)
    {
        RequireNumeric(left, right, operation);
        if (IsFloating(left) || IsFloating(right))
        {
            return floating(ExprValue.ToDouble(left), ExprValue.ToDouble(right));
        }

        return integer(ExprValue.ToInt64(left), ExprValue.ToInt64(right));
    }

    private static void RequireNumeric(object? left, object? right, string operation)
    {
        if ((!IsIntegral(left) && !IsFloating(left)) || (!IsIntegral(right) && !IsFloating(right)))
        {
            throw Error($"invalid operation: {TypeName(left)} {operation} {TypeName(right)}");
        }
    }

    private static bool IsIntegral(object? value) => value is
        sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint;

    private static bool IsFloating(object? value) => value is Half or float or double;

    private static bool TryGetInstant(object? value, out DateTimeOffset instant)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                instant = offset;
                return true;
            case DateTime dateTime:
                instant = dateTime.Kind switch
                {
                    DateTimeKind.Local => new DateTimeOffset(dateTime),
                    DateTimeKind.Utc => new DateTimeOffset(dateTime, TimeSpan.Zero),
                    _ => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc), TimeSpan.Zero),
                };
                return true;
            default:
                instant = default;
                return false;
        }
    }

    private static long NormalizeSliceIndex(long index, int length)
    {
        long result = index < 0 ? length + index : index;
        return Math.Clamp(result, 0, length);
    }

    private static string TypeName(object? value) => value?.GetType().FullName ?? "nil";

    private static string Display(object? value) => ExprDisplay.Value(value);

    private static ExprRuntimeException Error(string message, Exception? innerException = null) =>
        innerException is null
            ? new ExprRuntimeException(message)
            : new ExprRuntimeException(message, innerException);

    internal sealed record BoundMethod(object Target, MethodInfo Method);
}
