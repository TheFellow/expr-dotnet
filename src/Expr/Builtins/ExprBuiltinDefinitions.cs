using System;
using System.Collections.Generic;
using Expr.Runtime;
using Expr.Types;

namespace Expr.Builtins;

internal static class ExprBuiltinDefinitions
{
    private static readonly ArrayTypeDescriptor AnyArray = ExprTypes.ArrayOf(ExprTypes.Any);
    private static readonly MapTypeDescriptor AnyMap = new([], ExprTypes.Any, ExprTypes.Any);
    private static readonly ObjectTypeDescriptor TimeZone = new(typeof(TimeZoneInfo));
    private static readonly FunctionTypeDescriptor BooleanPredicate = new([ExprTypes.Any], ExprTypes.Boolean);
    private static readonly FunctionTypeDescriptor Projection = new([ExprTypes.Any], ExprTypes.Any);
    private static readonly FunctionTypeDescriptor Reducer = new([ExprTypes.Any, ExprTypes.Any], ExprTypes.Any);

    public static IReadOnlyList<ExprFunction> Create(ExprBuiltinLibrary library) =>
    [
        Predicate("all", O(ExprTypes.Boolean, false, AnyArray, BooleanPredicate)),
        Predicate("none", O(ExprTypes.Boolean, false, AnyArray, BooleanPredicate)),
        Predicate("any", O(ExprTypes.Boolean, false, AnyArray, BooleanPredicate)),
        Predicate("one", O(ExprTypes.Boolean, false, AnyArray, BooleanPredicate)),
        Predicate("filter", O(AnyArray, false, AnyArray, BooleanPredicate)),
        Predicate("map", O(AnyArray, false, AnyArray, Projection)),
        Predicate("find", O(ExprTypes.Any, false, AnyArray, BooleanPredicate)),
        Predicate("findIndex", O(ExprTypes.Integer, false, AnyArray, BooleanPredicate)),
        Predicate("findLast", O(ExprTypes.Any, false, AnyArray, BooleanPredicate)),
        Predicate("findLastIndex", O(ExprTypes.Integer, false, AnyArray, BooleanPredicate)),
        Predicate("count", O(ExprTypes.Integer, false, AnyArray, BooleanPredicate)),
        Predicate("sum", O(ExprTypes.Any, false, AnyArray), O(ExprTypes.Any, false, AnyArray, Projection)),
        Predicate("groupBy", O(AnyMap, false, AnyArray, Projection)),
        Predicate("sortBy", O(AnyArray, false, AnyArray, Projection), O(AnyArray, false, AnyArray, Projection, ExprTypes.String)),
        Predicate("reduce", O(ExprTypes.Any, false, AnyArray, Reducer), O(ExprTypes.Any, false, AnyArray, Reducer, ExprTypes.Any)),
        Function("len", ExprBuiltinValues.Len, [O(ExprTypes.Integer, false, ExprTypes.Any)], ValidateLength),
        Function("type", ExprBuiltinValues.TypeName, [O(ExprTypes.String, false, ExprTypes.Any)]),
        Function("abs", ExprBuiltinValues.Abs, [O(ExprTypes.Any, false, ExprTypes.Any)], ValidateNumericIdentity),
        Function("ceil", ExprBuiltinValues.Ceil, [O(ExprTypes.Float, false, ExprTypes.Any)], ValidateNumericFloat),
        Function("floor", ExprBuiltinValues.Floor, [O(ExprTypes.Float, false, ExprTypes.Any)], ValidateNumericFloat),
        Function("round", ExprBuiltinValues.Round, [O(ExprTypes.Float, false, ExprTypes.Any)], ValidateNumericFloat),
        Function("int", ExprBuiltinValues.Int, [O(ExprTypes.Integer, false, ExprTypes.Any)]),
        Function("float", ExprBuiltinValues.Float, [O(ExprTypes.Float, false, ExprTypes.Any)]),
        Safe("string", arguments => ExprBuiltinValues.StringSafe(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.Any)], ExprBuiltinValues.EstimateString),
        Safe("trim", arguments => ExprBuiltinStrings.TrimSafe(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String), O(ExprTypes.String, false, ExprTypes.String, ExprTypes.String)],
            ExprBuiltinStrings.EstimateTrim),
        Safe("trimPrefix", arguments => ExprBuiltinStrings.TrimPrefixSafe(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String), O(ExprTypes.String, false, ExprTypes.String, ExprTypes.String)],
            static arguments => ExprBuiltinStrings.EstimateInputCost(arguments, "trimPrefix")),
        Safe("trimSuffix", arguments => ExprBuiltinStrings.TrimSuffixSafe(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String), O(ExprTypes.String, false, ExprTypes.String, ExprTypes.String)],
            static arguments => ExprBuiltinStrings.EstimateInputCost(arguments, "trimSuffix")),
        Safe("upper", arguments => ExprBuiltinStrings.UpperSafe(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String)],
            static arguments => ExprBuiltinStrings.EstimateCasing(arguments, "upper")),
        Safe("lower", arguments => ExprBuiltinStrings.LowerSafe(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String)],
            static arguments => ExprBuiltinStrings.EstimateCasing(arguments, "lower")),
        Safe("split", arguments => ExprBuiltinStrings.SplitSafe(arguments, library.Options),
            [O(AnyArray, false, ExprTypes.String, ExprTypes.String), O(AnyArray, false, ExprTypes.String, ExprTypes.String, ExprTypes.Integer)],
            static arguments => ExprBuiltinStrings.EstimateSplit(arguments, "split")),
        Safe("splitAfter", arguments => ExprBuiltinStrings.SplitAfterSafe(arguments, library.Options),
            [O(AnyArray, false, ExprTypes.String, ExprTypes.String), O(AnyArray, false, ExprTypes.String, ExprTypes.String, ExprTypes.Integer)],
            static arguments => ExprBuiltinStrings.EstimateSplit(arguments, "splitAfter")),
        Safe("replace", arguments => ExprBuiltinStrings.ReplaceSafe(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String, ExprTypes.String, ExprTypes.String), O(ExprTypes.String, false, ExprTypes.String, ExprTypes.String, ExprTypes.String, ExprTypes.Integer)],
            ExprBuiltinStrings.EstimateReplace),
        Safe("repeat", arguments => ExprBuiltinStrings.Repeat(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String, ExprTypes.Integer)]),
        Safe("join", arguments => ExprBuiltinStrings.JoinSafe(arguments, library.Options),
            [O(ExprTypes.String, false, AnyArray), O(ExprTypes.String, false, AnyArray, ExprTypes.String)],
            ExprBuiltinStrings.EstimateJoin),
        Function("indexOf", ExprBuiltinStrings.IndexOf, [O(ExprTypes.Integer, false, ExprTypes.String, ExprTypes.String)]),
        Function("lastIndexOf", ExprBuiltinStrings.LastIndexOf, [O(ExprTypes.Integer, false, ExprTypes.String, ExprTypes.String)]),
        Function("hasPrefix", ExprBuiltinStrings.HasPrefix, [O(ExprTypes.Boolean, false, ExprTypes.String, ExprTypes.String)]),
        Function("hasSuffix", ExprBuiltinStrings.HasSuffix, [O(ExprTypes.Boolean, false, ExprTypes.String, ExprTypes.String)]),
        Function("max", arguments => ExprBuiltinCollections.MinMax(arguments, maximum: true, library.Options),
            [O(ExprTypes.Any, true, ExprTypes.Any)], ValidateAggregate),
        Function("min", arguments => ExprBuiltinCollections.MinMax(arguments, maximum: false, library.Options),
            [O(ExprTypes.Any, true, ExprTypes.Any)], ValidateAggregate),
        Function("mean", arguments => ExprBuiltinCollections.Mean(arguments, library.Options),
            [O(ExprTypes.Float, true, ExprTypes.Any)], ValidateAggregateFloat),
        Function("median", arguments => ExprBuiltinCollections.Median(arguments, library.Options),
            [O(ExprTypes.Float, true, ExprTypes.Any)], ValidateAggregateFloat),
        Safe("toJSON", arguments => ExprBuiltinSerialization.ToJson(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.Any)]),
        Safe("fromJSON", arguments => ExprBuiltinSerialization.FromJson(arguments, library.Options),
            [O(ExprTypes.Any, false, ExprTypes.String)]),
        Safe("toBase64", arguments => ExprBuiltinSerialization.ToBase64(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String)]),
        Safe("fromBase64", arguments => ExprBuiltinSerialization.FromBase64(arguments, library.Options),
            [O(ExprTypes.String, false, ExprTypes.String)]),
        Function("now", arguments => ExprBuiltinTime.Now(arguments, library.Options),
            [O(ExprTypes.Time, false), O(ExprTypes.Time, false, TimeZone)]),
        Function("duration", ExprBuiltinTime.Duration, [O(ExprTypes.Duration, false, ExprTypes.String)]),
        Function("date", arguments => ExprBuiltinTime.Date(arguments, library.Options),
            [
                O(ExprTypes.Time, false, ExprTypes.String),
                O(ExprTypes.Time, false, ExprTypes.String, ExprTypes.String),
                O(ExprTypes.Time, false, ExprTypes.String, ExprTypes.String, ExprTypes.String),
                O(ExprTypes.Time, false, TimeZone, ExprTypes.String),
                O(ExprTypes.Time, false, TimeZone, ExprTypes.String, ExprTypes.String),
            ], ValidateDate),
        Function("timezone", ExprBuiltinTime.Timezone, [O(TimeZone, false, ExprTypes.String)]),
        Function("first", ExprBuiltinCollections.First, [O(ExprTypes.Any, false, AnyArray)]),
        Function("last", ExprBuiltinCollections.Last, [O(ExprTypes.Any, false, AnyArray)]),
        Function("get", ExprBuiltinCollections.Get,
            [O(ExprTypes.Any, false, ExprTypes.Any, ExprTypes.Any)]),
        Safe("take", arguments => ExprBuiltinCollections.Take(arguments, library.Options),
            [O(AnyArray, false, AnyArray, ExprTypes.Integer)], validator: ValidateTake),
        Safe("keys", arguments => ExprBuiltinCollections.Keys(arguments, library.Options), [O(AnyArray, false, AnyMap)]),
        Safe("values", arguments => ExprBuiltinCollections.Values(arguments, library.Options), [O(AnyArray, false, AnyMap)]),
        Safe("toPairs", arguments => ExprBuiltinCollections.ToPairs(arguments, library.Options), [O(AnyArray, false, AnyMap)]),
        Safe("fromPairs", arguments => ExprBuiltinCollections.FromPairs(arguments, library.Options), [O(AnyMap, false, AnyArray)]),
        Safe("reverse", arguments => ExprBuiltinCollections.Reverse(arguments, library.Options), [O(AnyArray, false, AnyArray)]),
        Safe("uniq", arguments => ExprBuiltinCollections.Unique(arguments, library.Options), [O(AnyArray, false, AnyArray)]),
        Safe("concat", arguments => ExprBuiltinCollections.Concat(arguments, library.Options), [O(AnyArray, true, AnyArray)]),
        Safe("flatten", arguments => ExprBuiltinCollections.Flatten(arguments, library.Options), [O(AnyArray, false, AnyArray)]),
        Safe("sort", arguments => ExprBuiltinCollections.Sort(arguments, library.Options),
            [O(AnyArray, false, AnyArray), O(AnyArray, false, AnyArray, ExprTypes.String)]),
        Function("bitand", arguments => ExprBuiltinValues.BinaryBit(arguments, "bitand", static (left, right) => left & right),
            [O(ExprTypes.Integer, false, ExprTypes.Integer, ExprTypes.Integer)]),
        Function("bitor", arguments => ExprBuiltinValues.BinaryBit(arguments, "bitor", static (left, right) => left | right),
            [O(ExprTypes.Integer, false, ExprTypes.Integer, ExprTypes.Integer)]),
        Function("bitxor", arguments => ExprBuiltinValues.BinaryBit(arguments, "bitxor", static (left, right) => left ^ right),
            [O(ExprTypes.Integer, false, ExprTypes.Integer, ExprTypes.Integer)]),
        Function("bitnand", arguments => ExprBuiltinValues.BinaryBit(arguments, "bitnand", static (left, right) => left & ~right),
            [O(ExprTypes.Integer, false, ExprTypes.Integer, ExprTypes.Integer)]),
        Function("bitshl", arguments => ExprBuiltinValues.Shift(arguments, "bitshl", left: true, unsigned: false),
            [O(ExprTypes.Integer, false, ExprTypes.Integer, ExprTypes.Integer)]),
        Function("bitshr", arguments => ExprBuiltinValues.Shift(arguments, "bitshr", left: false, unsigned: false),
            [O(ExprTypes.Integer, false, ExprTypes.Integer, ExprTypes.Integer)]),
        Function("bitushr", arguments => ExprBuiltinValues.Shift(arguments, "bitushr", left: false, unsigned: true),
            [O(ExprTypes.Integer, false, ExprTypes.Integer, ExprTypes.Integer)]),
        Function("bitnot", ExprBuiltinValues.BitNot, [O(ExprTypes.Integer, false, ExprTypes.Integer)]),
    ];

    private static ExprFunction Predicate(string name, params ExprFunctionOverload[] overloads) => new(
        name,
        overloads,
        isPredicate: true);

    private static ExprFunction Function(
        string name,
        ExprFunctionInvoker invoker,
        IReadOnlyList<ExprFunctionOverload> overloads,
        ExprFunctionTypeValidator? validator = null) => new(
            name,
            overloads,
            invoker,
            safeInvoker: null,
            typeValidator: validator,
            isPredicate: false,
            memoryEstimator: null,
            enforceRuntimeArity: false);

    private static ExprFunction Safe(
        string name,
        ExprSafeFunctionInvoker invoker,
        IReadOnlyList<ExprFunctionOverload> overloads,
        ExprFunctionMemoryEstimator? estimator = null,
        ExprFunctionTypeValidator? validator = null) => new(
            name,
            overloads,
            invoker: null,
            safeInvoker: invoker,
            typeValidator: validator,
            isPredicate: false,
            memoryEstimator: estimator,
            enforceRuntimeArity: false);

    private static ExprFunctionOverload O(
        ExprTypeDescriptor result,
        bool variadic,
        params ExprTypeDescriptor[] parameters) => ExprBuiltinLibrary.Overload(result, variadic, parameters);

    private static ExprTypeDescriptor ValidateLength(ReadOnlySpan<ExprTypeDescriptor> arguments)
    {
        RequireArity(arguments, 1);
        return arguments[0] switch
        {
            ArrayTypeDescriptor or MapTypeDescriptor => ExprTypes.Integer,
            _ when arguments[0].Kind is ExprTypeKind.String or ExprTypeKind.Any => ExprTypes.Integer,
            _ => throw new ExprRuntimeException($"invalid argument for len (type {arguments[0]})"),
        };
    }

    private static ExprTypeDescriptor ValidateNumericIdentity(ReadOnlySpan<ExprTypeDescriptor> arguments)
    {
        RequireArity(arguments, 1);
        if (arguments[0].Kind is not (ExprTypeKind.Integer or ExprTypeKind.Float or ExprTypeKind.Any))
        {
            throw new ExprRuntimeException($"invalid argument for abs (type {arguments[0]})");
        }

        return arguments[0];
    }

    private static ExprTypeDescriptor ValidateNumericFloat(ReadOnlySpan<ExprTypeDescriptor> arguments)
    {
        RequireArity(arguments, 1);
        if (arguments[0].Kind is not (ExprTypeKind.Integer or ExprTypeKind.Float or ExprTypeKind.Any))
        {
            throw new ExprRuntimeException($"invalid numeric argument (type {arguments[0]})");
        }

        return ExprTypes.Float;
    }

    private static ExprTypeDescriptor ValidateAggregate(ReadOnlySpan<ExprTypeDescriptor> arguments)
    {
        if (arguments.IsEmpty)
        {
            throw new ExprRuntimeException("not enough arguments to call aggregate");
        }

        foreach (ExprTypeDescriptor argument in arguments)
        {
            if (argument.Kind is ExprTypeKind.Any or ExprTypeKind.Array)
            {
                return ExprTypes.Any;
            }
        }

        return arguments[0];
    }

    private static ExprTypeDescriptor ValidateAggregateFloat(ReadOnlySpan<ExprTypeDescriptor> arguments)
    {
        return ValidateAggregate(arguments);
    }

    private static ExprTypeDescriptor ValidateTake(ReadOnlySpan<ExprTypeDescriptor> arguments)
    {
        RequireArity(arguments, 2);
        return arguments[0].Kind is ExprTypeKind.Any ? ExprTypes.Any : AnyArray;
    }

    private static ExprTypeDescriptor ValidateDate(ReadOnlySpan<ExprTypeDescriptor> arguments)
    {
        if (arguments.Length is < 1 or > 4)
        {
            throw new ExprRuntimeException(
                $"invalid number of arguments (expected between 1 and 4, got {arguments.Length})");
        }

        return ExprTypes.Time;
    }

    private static void RequireArity(ReadOnlySpan<ExprTypeDescriptor> arguments, int count)
    {
        if (arguments.Length != count)
        {
            throw new ExprRuntimeException($"invalid number of arguments (expected {count}, got {arguments.Length})");
        }
    }
}
