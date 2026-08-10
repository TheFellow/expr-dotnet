using System;
using System.Linq;
using Expr.Builtins;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Builtins;

public sealed class BuiltinRegistryTests
{
    private static readonly string[] ExpectedNames =
    [
        "all", "none", "any", "one", "filter", "map", "find", "findIndex", "findLast",
        "findLastIndex", "count", "sum", "groupBy", "sortBy", "reduce", "len", "type",
        "abs", "ceil", "floor", "round", "int", "float", "string", "trim", "trimPrefix",
        "trimSuffix", "upper", "lower", "split", "splitAfter", "replace", "repeat", "join",
        "indexOf", "lastIndexOf", "hasPrefix", "hasSuffix", "max", "min", "mean", "median",
        "toJSON", "fromJSON", "toBase64", "fromBase64", "now", "duration", "date", "timezone",
        "first", "last", "get", "take", "keys", "values", "toPairs", "fromPairs", "reverse",
        "uniq", "concat", "flatten", "sort", "bitand", "bitor", "bitxor", "bitnand", "bitshl",
        "bitshr", "bitushr", "bitnot",
    ];

    [Fact]
    public void Registry_matches_every_pinned_upstream_builtin_in_order()
    {
        ExprBuiltinLibrary library = ExprBuiltinLibrary.Standard;

        Assert.Equal(71, library.Functions.Count);
        Assert.Equal(ExpectedNames, library.Names);
        Assert.Equal(ExpectedNames, library.Functions.Select(static function => function.Name));
        Assert.Equal(ExpectedNames.Length, library.Names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Lookup_is_ordinal_and_reports_unknown_names()
    {
        ExprBuiltinLibrary library = ExprBuiltinLibrary.Standard;

        Assert.True(library.TryGet("len", out _));
        Assert.False(library.TryGet("LEN", out _));
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => library.Get("LEN"));
    }

    [Fact]
    public void Predicate_metadata_identifies_all_vm_supplied_functions()
    {
        ExprBuiltinLibrary library = ExprBuiltinLibrary.Standard;

        Assert.Equal(15, library.Functions.Count(static function => function.IsPredicate));
        Assert.All(library.Functions.Take(15), static function => Assert.True(function.IsPredicate));
        Assert.All(library.Functions.Skip(15), static function => Assert.False(function.IsPredicate));
    }

    [Fact]
    public void Predicate_metadata_exposes_projection_reducer_and_optional_overloads()
    {
        ExprBuiltinLibrary library = ExprBuiltinLibrary.Standard;

        Assert.IsType<FunctionTypeDescriptor>(library.Get("all").Overloads[0].Parameters[1]);
        Assert.Equal(2, library.Get("sum").Overloads.Count);
        Assert.Equal(2, library.Get("sortBy").Overloads.Count);
        FunctionTypeDescriptor reducer = Assert.IsType<FunctionTypeDescriptor>(
            library.Get("reduce").Overloads[0].Parameters[1]);
        Assert.Equal(2, reducer.Parameters.Count);
        Assert.Same(ExprTypes.Any, reducer.ReturnType);
    }
}
