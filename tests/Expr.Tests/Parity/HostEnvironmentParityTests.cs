using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Parity;

public sealed class HostEnvironmentParityTests
{
    [Fact]
    public void Nullable_members_optional_chains_and_public_members_are_safe()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<NullableEnvironment>()
            .Member("Student", static environment => environment.Student, new ObjectTypeDescriptor(typeof(Student)))
            .Member("Enabled", static environment => environment.Enabled, ExprTypes.Nullable(ExprTypes.Boolean))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        var absent = new NullableEnvironment(null, null);
        var present = new NullableEnvironment(new Student("Ada"), true);

        Assert.Null(ExprEngine.Evaluate("Student?.Name", absent, configuration, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Ada", ExprEngine.Evaluate("Student?.Name", present, configuration, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("default", ExprEngine.Evaluate("Enabled == nil ? 'default' : (Enabled ? 'enabled' : 'disabled')", absent, configuration, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("enabled", ExprEngine.Evaluate("Enabled == nil ? 'default' : (Enabled ? 'enabled' : 'disabled')", present, configuration, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Variadic_host_method_accepts_multiple_arguments()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<MethodEnvironment>()
            .Member("Container", static environment => environment.Container, new ObjectTypeDescriptor(typeof(Container)))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        object? value = ExprEngine.Evaluate(
            "Container.IncludesAny('nope', 'again', 'bar')",
            new MethodEnvironment(new Container(["foo", "bar", "baz"])),
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(true, value);
    }

    [Fact]
    public void Runtime_function_can_replace_disabled_builtin()
    {
        var replacement = new ExprFunction(
            "upper",
            [new ExprFunctionOverload([ExprTypes.Integer], ExprTypes.Integer)],
            static arguments => arguments[0]);
        ExprConfiguration configuration = ExprConfiguration.Default.DisableBuiltin("upper").WithFunction(replacement);

        Assert.Equal(1L, ExprEngine.Evaluate(
            "upper(1)",
            configuration: configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Dynamic_runtime_function_can_replace_disabled_builtin()
    {
        ExprConfiguration configuration = ExprConfiguration.Default
            .DisableBuiltin("upper")
            .AllowUndefinedVariables();
        var environment = new Dictionary<string, object?>
        {
            ["upper"] = new Func<long, long>(static value => value),
        };

        Assert.Equal(1L, ExprEngine.Evaluate(
            "upper(1)",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Context_is_injected_into_nested_registered_functions()
    {
        var environment = new ContextEnvironment(TestContext.Current.CancellationToken);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ContextEnvironment>()
            .Member("ctx", static value => value.Context, new ObjectTypeDescriptor(typeof(CancellationToken)))
            .Build();
        bool nowCalled = false;
        bool dateCalled = false;
        ExprTypeDescriptor cancellationType = new ObjectTypeDescriptor(typeof(CancellationToken));
        var now = new ExprFunction(
            "now2",
            [new ExprFunctionOverload([cancellationType], ExprTypes.Time)],
            arguments =>
            {
                _ = Assert.IsType<CancellationToken>(arguments[0]);
                nowCalled = true;
                return new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            });
        var date = new ExprFunction(
            "date2",
            [new ExprFunctionOverload([cancellationType], ExprTypes.Time)],
            arguments =>
            {
                _ = Assert.IsType<CancellationToken>(arguments[0]);
                dateCalled = true;
                return new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            });
        var after = new ExprFunction(
            "after",
            [new ExprFunctionOverload([ExprTypes.Time, ExprTypes.Time, cancellationType], ExprTypes.Boolean)],
            static arguments => (DateTimeOffset)arguments[0]! > (DateTimeOffset)arguments[1]!);
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithFunction(now)
            .WithFunction(date)
            .WithFunction(after)
            .WithContext("ctx");

        Assert.Equal(true, ExprEngine.Evaluate(
            "after(now2(), date2())",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(nowCalled);
        Assert.True(dateCalled);
    }

    [Fact]
    public void Context_is_injected_into_environment_and_nested_object_methods()
    {
        var environment = new ContextMethodEnvironment(
            new RpcGroup(new Rpc()),
            "hello",
            TestContext.Current.CancellationToken);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ContextMethodEnvironment>()
            .Member("ctx", static value => value.Context, new ObjectTypeDescriptor(typeof(CancellationToken)))
            .Member("g", static value => value.Group, new ObjectTypeDescriptor(typeof(RpcGroup)))
            .Member("text", static value => value.Text, ExprTypes.String)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema).WithContext("ctx");

        Assert.Equal(true, ExprEngine.Evaluate(
            "Now2() > Date2()",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("hello", ExprEngine.Evaluate(
            "let v = g.Rpc.HelloCtx(text); v",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Operator_overrides_compose_custom_decimal_values()
    {
        ExprTypeDescriptor decimalType = new ObjectTypeDescriptor(typeof(DecimalValue));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<DecimalEnvironment>()
            .Member("A", static value => value.A, decimalType)
            .Member("B", static value => value.B, decimalType)
            .Member("C", static value => value.C, decimalType)
            .Build();
        var add = new ExprFunction(
            "addDecimal",
            [new ExprFunctionOverload([decimalType, decimalType], decimalType)],
            static arguments => new DecimalValue(
                ((DecimalValue)arguments[0]!).Number + ((DecimalValue)arguments[1]!).Number));
        var subtract = new ExprFunction(
            "subtractDecimal",
            [new ExprFunctionOverload([decimalType, decimalType], decimalType)],
            static arguments => new DecimalValue(
                ((DecimalValue)arguments[0]!).Number - ((DecimalValue)arguments[1]!).Number));
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithFunction(add)
            .WithFunction(subtract)
            .WithOperator("+", "addDecimal")
            .WithOperator("-", "subtractDecimal");

        object? result = ExprEngine.Evaluate(
            "A + B - C",
            new DecimalEnvironment(new DecimalValue(10), new DecimalValue(5), new DecimalValue(3)),
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new DecimalValue(12), result);
    }

    [Fact]
    public void Typed_interface_methods_accept_nil_arguments()
    {
        var environment = new InterfaceEnvironment(new Foo());
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<InterfaceEnvironment>()
            .Member("foo", static value => value.Foo, new ObjectTypeDescriptor(typeof(IFoo)))
            .Build();

        Assert.Equal(1L, ExprEngine.Evaluate(
            "foo.Add(1, nil)",
            environment,
            ExprConfiguration.Default.WithEnvironment(schema),
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Clr_numeric_widths_and_derived_collections_match_host_regressions()
    {
        (object Left, object Right)[] numericPairs =
        [
            ((sbyte)1, (short)3),
            (5, 7L),
            ((byte)11, (ushort)13),
            (17U, 19UL),
            (97F, 101D),
        ];
        foreach ((object left, object right) in numericPairs)
        {
            object? value = ExprEngine.Evaluate(
                "left / right",
                new Dictionary<string, object?> { ["left"] = left, ["right"] = right },
                ExprConfiguration.Default.AllowUndefinedVariables(),
                cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.IsType<double>(value);
        }

        var sum = new Func<uint, ushort, byte, ulong, int, short, sbyte, long, double, float, double>(
            static (a, b, c, d, e, f, g, h, i, j) => (double)a + b + c + d + e + f + g + h + i + j);
        Assert.Equal(10D, ExprEngine.Evaluate(
            "combine(1, 1, 1, 1, 1, 1, 1, 1, 1, 1)",
            new Dictionary<string, object?> { ["combine"] = sum },
            ExprConfiguration.Default.AllowUndefinedVariables(),
            cancellationToken: TestContext.Current.CancellationToken));

        var values = new DerivedValues { 1D, 2D, 3D };
        var items = new DerivedItems { new Item("bar") };
        var collections = new DerivedCollectionEnvironment(values, items);
        ExprEnvironmentSchema collectionSchema = new ExprEnvironmentSchemaBuilder<DerivedCollectionEnvironment>()
            .Member("values", static value => value.Values)
            .Member("items", static value => value.Items)
            .Build();
        ExprConfiguration collectionConfiguration = ExprConfiguration.Default.WithEnvironment(collectionSchema);
        Assert.Equal(1D, ExprEngine.Evaluate(
            "values[0]",
            collections,
            collectionConfiguration,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("bar", ExprEngine.Evaluate(
            "items[0].Bar",
            collections,
            collectionConfiguration,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1D, ExprEngine.Evaluate(
            "values[0]",
            new Dictionary<string, object?> { ["values"] = values },
            ExprConfiguration.Default.AllowUndefinedVariables(),
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Nullable_enum_conversion_and_dynamic_comparison_match_host_regressions()
    {
        var environment = new EnumEnvironment(Mode.A);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<EnumEnvironment>()
            .Member("Mode", static value => value.Mode)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        Assert.Equal(true, ExprEngine.Evaluate(
            "int(Mode) == 1",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<Expr.Checking.ExprCheckException>(() => ExprEngine.Compile("Mode == 1", configuration));
        Assert.Equal(false, ExprEngine.Evaluate(
            "Mode == 1",
            new Dictionary<string, object?> { ["Mode"] = Mode.A },
            ExprConfiguration.Default.AllowUndefinedVariables(),
            cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed record Student(string Name);

    private sealed record NullableEnvironment(Student? Student, bool? Enabled);

    private sealed record MethodEnvironment(Container Container);

    private sealed record ContextEnvironment(CancellationToken Context);

    private sealed record ContextMethodEnvironment(
        RpcGroup Group,
        string Text,
        CancellationToken Context)
    {
        public DateTimeOffset Now2(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled
                ? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : DateTimeOffset.MinValue;

        public DateTimeOffset Date2(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled
                ? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : DateTimeOffset.MaxValue;
    }

    private sealed record RpcGroup(Rpc Rpc);

    private sealed record DecimalValue(decimal Number);

    private sealed record DecimalEnvironment(DecimalValue A, DecimalValue B, DecimalValue C);

    private sealed record InterfaceEnvironment(IFoo Foo);

    private interface IFoo
    {
        long Add(long value, long? optional);
    }

    private sealed class Foo : IFoo
    {
        public long Add(long value, long? optional) => value + (optional ?? 0);
    }

    private sealed class DerivedValues : List<double>
    {
    }

    private sealed class DerivedItems : List<Item>
    {
    }

    private sealed record Item(string Bar);

    private sealed record DerivedCollectionEnvironment(DerivedValues Values, DerivedItems Items);

    private sealed record EnumEnvironment(Mode? Mode);

    private enum Mode
    {
        A = 1,
    }

    private sealed class Rpc
    {
        public string HelloCtx(string text, CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? text : string.Empty;
    }

    private sealed class Container(IReadOnlyList<string> values)
    {
        public bool IncludesAny(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (values.Contains(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
