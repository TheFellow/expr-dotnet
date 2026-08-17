using System;
using System.Collections.Generic;
using Expr.Checking;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Facade;

public sealed class PublicApiBehaviorCoverageTests
{
    private static readonly ExprConfiguration DynamicConfiguration = ExprConfiguration.Default
        .AllowUndefinedVariables()
        .WithOptimization(false);

    [Theory]
    [InlineData("all(values[1:], # > 1) and all(values[1:], # < 4)")]
    [InlineData("all(flag ? values : other, # > 1) and all(flag ? values : other, # < 4)")]
    [InlineData("all(filter(values, # > 0), # > 1) and all(filter(values, # > 0), # < 4)")]
    [InlineData("all(load(), # > 1) and all(load(), # < 4)")]
    public void Public_pipeline_combines_predicates_over_equivalent_computed_collections(string source)
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["values"] = new object?[] { 2L, 3L },
            ["other"] = new object?[] { 2L, 3L },
            ["flag"] = true,
            ["load"] = new Func<object?[]>(static () => [2L, 3L]),
        };
        ExprConfiguration configuration = ExprConfiguration.Default.AllowUndefinedVariables();

        CompiledExpression expression = ExprEngine.Compile(source, configuration);

        Assert.IsType<BuiltinNode>(expression.SyntaxTree.Root);
        Assert.Equal(
            true,
            expression.Run(environment, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Public_checker_accepts_compatible_nullable_collection_and_object_contracts()
    {
        _ = new TypeEnvironment(null);
        _ = new DerivedValue();

        AssertContract(ExprTypes.Nullable(ExprTypes.Integer), ExprTypes.Integer);
        AssertContract(ExprTypes.Integer, ExprTypes.Nullable(ExprTypes.Float));
        AssertContract(ExprTypes.Nil, ExprTypes.Nullable(ExprTypes.String));
        AssertContract(ExprTypes.Nil, ExprTypes.ArrayOf(ExprTypes.Integer));
        AssertContract(ExprTypes.Nil, new MapTypeDescriptor([], ExprTypes.Integer));
        AssertContract(ExprTypes.Nil, new ObjectTypeDescriptor(typeof(BaseValue)));
        AssertContract(ExprTypes.Nil, new FunctionTypeDescriptor([], ExprTypes.Integer));
        AssertContract(ExprTypes.ArrayOf(ExprTypes.Integer), ExprTypes.ArrayOf(ExprTypes.Float));
        AssertContract(new ObjectTypeDescriptor(typeof(DerivedValue)), new ObjectTypeDescriptor(typeof(BaseValue)));
    }

    [Fact]
    public void Public_checker_validates_open_and_strict_map_contracts()
    {
        var openIntegers = new MapTypeDescriptor(
            [new KeyValuePair<string, ExprTypeDescriptor>("count", ExprTypes.Integer)],
            ExprTypes.Integer);
        var openFloats = new MapTypeDescriptor([], ExprTypes.Float);
        var strictFloats = new MapTypeDescriptor(
            [new KeyValuePair<string, ExprTypeDescriptor>("count", ExprTypes.Float)]);
        var incompatible = new MapTypeDescriptor([], ExprTypes.String);

        AssertContract(openIntegers, openFloats);
        AssertContract(openIntegers, strictFloats);
        AssertContract(new MapTypeDescriptor([], ExprTypes.Integer), strictFloats);

        ExprCheckException exception = Assert.Throws<ExprCheckException>(() =>
            CheckContract(openIntegers, incompatible));
        Assert.Contains("expected Map", exception.Message, StringComparison.Ordinal);

        Assert.Throws<ExprCheckException>(() => CheckContract(
            new MapTypeDescriptor(
                [new KeyValuePair<string, ExprTypeDescriptor>("count", ExprTypes.String)],
                ExprTypes.Integer),
            openFloats));
        Assert.Throws<ExprCheckException>(() => CheckContract(
            new MapTypeDescriptor(
                [new KeyValuePair<string, ExprTypeDescriptor>("count", ExprTypes.String)]),
            strictFloats));
        Assert.Throws<ExprCheckException>(() => CheckContract(
            new MapTypeDescriptor([]),
            strictFloats));
    }

    [Fact]
    public void Public_checker_finds_common_nested_collection_types_and_compares_arrays()
    {
        _ = new BooleanEnvironment(false);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<BooleanEnvironment>()
            .Member("flag", static environment => environment.Flag, ExprTypes.Boolean)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
        ExprSemanticModel nested = ExprEngine.Check("flag ? [[1]] : [[2.5]]", configuration);
        ExprTypeDescriptor expected = ExprTypes.ArrayOf(ExprTypes.ArrayOf(ExprTypes.Float));

        Assert.True(expected.IsEquivalentTo(nested.ResultType));
        Assert.Same(ExprTypes.Boolean, ExprEngine.Check("[1] == ['one']").ResultType);
        Assert.Same(ExprTypes.String, ExprEngine.Check("flag ? nil : 'value'", configuration).ResultType);
        Assert.Same(ExprTypes.String, ExprEngine.Check("flag ? 'value' : nil", configuration).ResultType);
        Assert.Same(ExprTypes.Any, ExprEngine.Check("flag ? 'value' : 42", configuration).ResultType);
    }

    [Fact]
    public void Public_dynamic_evaluation_supports_all_documented_clr_numeric_families()
    {
        (object Input, object Expected)[] cases =
        [
            ((sbyte)2, (sbyte)-2),
            ((byte)2, (byte)254),
            ((short)2, (short)-2),
            ((ushort)2, (ushort)65534),
            (2, -2),
            (2U, uint.MaxValue - 1),
            (2L, -2L),
            (2UL, ulong.MaxValue - 1),
            ((nint)2, (nint)(-2)),
            ((nuint)2, unchecked((nuint)(-2))),
            ((Half)2, (Half)(-2)),
            (2F, -2F),
            (2D, -2D),
        ];

        foreach ((object input, object expected) in cases)
        {
            Assert.Equal(expected, Evaluate("-value", ("value", input)));
        }

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            Evaluate("-value", ("value", "two")));
        Assert.Contains("invalid operation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_dynamic_evaluation_supports_temporal_arithmetic_in_both_operand_orders()
    {
        var instant = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        TimeSpan duration = TimeSpan.FromMinutes(30);
        TimeSpan otherDuration = TimeSpan.FromMinutes(10);
        var otherInstant = instant.AddMinutes(-5);

        Assert.Equal(instant.Add(duration), Evaluate("instant + duration", ("instant", instant), ("duration", duration)));
        Assert.Equal(instant.Add(duration), Evaluate("duration + instant", ("instant", instant), ("duration", duration)));
        Assert.Equal(duration + otherDuration, Evaluate(
            "duration + other",
            ("duration", duration),
            ("other", otherDuration)));
        Assert.Equal(instant - otherInstant, Evaluate(
            "instant - other",
            ("instant", instant),
            ("other", otherInstant)));
        Assert.Equal(instant - duration, Evaluate("instant - duration", ("instant", instant), ("duration", duration)));
        Assert.Equal(duration - otherDuration, Evaluate(
            "duration - other",
            ("duration", duration),
            ("other", otherDuration)));
        Assert.Equal(duration * 2, Evaluate("duration * 2", ("duration", duration)));
        Assert.Equal(duration * 2, Evaluate("2 * duration", ("duration", duration)));
    }

    [Fact]
    public void Public_dynamic_evaluation_supports_host_byte_memory_for_index_slice_and_match()
    {
        byte[] array = "hello"u8.ToArray();
        var readOnly = new ReadOnlyMemory<byte>(array);
        var writable = new Memory<byte>(array);

        Assert.Equal((byte)'o', Evaluate("value[-1]", ("value", array)));
        Assert.Equal((byte)'e', Evaluate("value[1]", ("value", readOnly)));
        Assert.Equal((byte)'l', Evaluate("value[2]", ("value", writable)));

        var slice = Assert.IsType<ReadOnlyMemory<byte>>(Evaluate("value[1:4]", ("value", writable)));
        Assert.Equal("ell"u8.ToArray(), slice.ToArray());
        Assert.Equal(true, Evaluate("value matches pattern", ("value", array), ("pattern", "^h.*o$")));
        Assert.Equal(true, Evaluate("value matches pattern", ("value", readOnly), ("pattern", "ell")));

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            Evaluate("value[99]", ("value", array)));
        Assert.Contains("index out of range", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_dynamic_evaluation_reports_invalid_host_operations_at_the_expression_boundary()
    {
        AssertRuntimeError("value[0]", "cannot fetch", ("value", null));
        AssertRuntimeError("value['key']", "cannot fetch", ("value", "text"));
        AssertRuntimeError("left % right", "invalid operation", ("left", 1.5), ("right", 1L));
        AssertRuntimeError(
            "value matches pattern",
            "pattern type",
            ("value", "text"),
            ("pattern", 42L));
        AssertRuntimeError(
            "value matches pattern",
            "input type",
            ("value", 42L),
            ("pattern", "text"));
        AssertRuntimeError("value()", "cannot call non-function", ("value", 42L));
        AssertRuntimeError(
            "function(1)",
            "invalid number of arguments",
            ("function", new Func<long, long, long>(static (left, right) => left + right)));
    }

    private static void AssertContract(ExprTypeDescriptor valueType, ExprTypeDescriptor expectedType)
    {
        ExprSemanticModel model = CheckContract(valueType, expectedType);
        Assert.Same(valueType, model.ResultType);
    }

    private static ExprSemanticModel CheckContract(
        ExprTypeDescriptor valueType,
        ExprTypeDescriptor expectedType)
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<TypeEnvironment>()
            .Member("value", static environment => environment.Value, valueType)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithExpectedType(expectedType, warnOnAny: true);
        return ExprEngine.Check("value", configuration);
    }

    private static object? Evaluate(string source, params (string Name, object? Value)[] values)
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in values)
        {
            environment.Add(name, value);
        }

        return ExprEngine.Evaluate(
            source,
            environment,
            DynamicConfiguration,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static void AssertRuntimeError(
        string source,
        string message,
        params (string Name, object? Value)[] values)
    {
        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() => Evaluate(source, values));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    private sealed record TypeEnvironment(object? Value);

    private sealed record BooleanEnvironment(bool Flag);

    private abstract class BaseValue;

    private sealed class DerivedValue : BaseValue;
}
