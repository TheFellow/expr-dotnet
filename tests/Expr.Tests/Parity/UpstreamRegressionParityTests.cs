using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Expr.Checking;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Parity;

// Exact regression families from expr_test.go and test/issues/* at upstream
// revision 4b31df3a2e0eefec04c017a82a00e0f08541d3e4.
public sealed class UpstreamRegressionParityTests
{
    private static readonly ExprConfiguration DynamicConfiguration = ExprConfiguration.Default.AllowUndefinedVariables();

    public static TheoryData<string, object?> PureExpressions => new()
    {
        { "all(1..3, all(1..3, # > 0) and # > 0)", true },
        { "1 / (1 - 1)", double.PositiveInfinity },
        { "get({}, 'missing')", null },
        { "get({}, 'a') | get('b') | get('c')", null },
        { "let a = [1]; let b = type(a[0]) == 'array' ? a : [a]; b[0][0]", 1L },
        { "let range = [1, 1000]; let arr = false ? range : [range]; map(arr, len(#))", new object?[] { 2L } },
        { "[0, 1, 2, 3, 4][1:2][0]", 1L },
        { "one([{Name: 'one'}, {Name: 'two'}], .Name in ['one'])", true },
        {
            "[true && true, one([{Name: 'one'}, {Name: 'two'}], .Name in ['one']), one([{Name: 'one'}, {Name: 'two'}], .Name in ['two']), one([{Name: 'one'}, {Name: 'two'}], .Name in ['one']) && one([{Name: 'one'}, {Name: 'two'}], .Name in ['two'])]",
            new object?[] { true, true, true, true }
        },
        { "int(1) == 1", true },
        { "fromJSON('{\"Num\":1}').Num", 1L },
    };

    [Theory]
    [MemberData(nameof(PureExpressions))]
    public void Upstream_pure_regression_expressions_evaluate(string source, object? expected)
    {
        object? actual = ExprEngine.Evaluate(
            source,
            configuration: DynamicConfiguration,
            cancellationToken: TestContext.Current.CancellationToken);

        if (expected is object?[] expectedArray)
        {
            Assert.Equal(expectedArray, [.. Assert.IsAssignableFrom<IExprArray>(actual)]);
        }
        else if (expected is long expectedInteger && actual is not null)
        {
            Assert.Equal(expectedInteger, Convert.ToInt64(actual, CultureInfo.InvariantCulture));
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Dynamic_environment_covers_arithmetic_maps_pipes_and_missing_values()
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["a"] = 1L,
            ["b"] = 2L,
            ["c"] = 3L,
            ["d"] = 4L,
            ["arr"] = new long[] { 0, 1, 2, 3, 4 },
            ["empty_map"] = new Dictionary<string, object?>(StringComparer.Ordinal),
            ["foo"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["entry"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["alpha"] = "x",
                    ["beta"] = 1L,
                },
            },
            ["bar"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["entry"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["alpha"] = "x",
                    ["beta"] = 1L,
                },
            },
            ["json"] = "{\"Num\":1}",
            ["vars"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = "value" },
        };

        Assert.Equal(0.5D, Evaluate("(a - b + c) / d", environment));
        Assert.Null(Evaluate("get(empty_map, 'missing')", environment));
        Assert.Equal(1L, Evaluate("arr[1:2][0]", environment));
        Assert.Equal("x", Evaluate("{'value': 'x'}[foo.entry.alpha == 'x' ? 'value' : 'missing']", environment));
        Assert.Equal("ok", Evaluate("{'value': 'ok'}[vars.key]", environment));
        IExprMap parsedJson = Assert.IsAssignableFrom<IExprMap>(Evaluate("fromJSON(json)", environment));
        Assert.True(parsedJson.TryGetValue("Num", out _));
        object? pipeline = Evaluate(
            "foo | keys() | filter(# in bar) | filter(foo[#].alpha == bar[#].alpha) | filter(foo[#].beta == bar[#].beta)",
            environment);
        Assert.Equal(["entry"], Assert.IsAssignableFrom<IExprArray>(pipeline).ToArray());
    }

    [Fact]
    public void Readme_function_environment_example_executes()
    {
        var format = new ExprFunction(
            "sprintf",
            [new ExprFunctionOverload([ExprTypes.String, ExprTypes.Any], ExprTypes.String, isVariadic: true)],
            static arguments => ((string)arguments[0]!).Replace(
                "%v",
                Convert.ToString(arguments[1], CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
        ExprConfiguration configuration = DynamicConfiguration.WithFunction(format);
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["greet"] = "Hello, %v!",
            ["names"] = new[] { "world", "you" },
        };

        Assert.Equal("Hello, world!", ExprEngine.Evaluate(
            "sprintf(greet, names[0])",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Compile_time_integer_modulo_by_zero_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => ExprEngine.Compile("1 % 0"));
    }

    [Fact]
    public void Expected_result_contracts_cover_typed_result_examples()
    {
        var expectedMapType = new MapTypeDescriptor(
            [
                new KeyValuePair<string, ExprTypeDescriptor>("a", ExprTypes.Integer),
                new KeyValuePair<string, ExprTypeDescriptor>("b", ExprTypes.Integer),
            ]);
        object? map = ExprEngine.Evaluate(
            "{a: 1, b: 2}",
            configuration: ExprConfiguration.Default.WithExpectedType(expectedMapType),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsAssignableFrom<IExprMap>(map);
        Assert.Equal(true, ExprEngine.Evaluate(
            "1 >= 0",
            configuration: ExprConfiguration.Default.WithExpectedType(ExprTypes.Boolean),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(42L, ExprEngine.Evaluate(
            "42",
            configuration: ExprConfiguration.Default.WithExpectedType(ExprTypes.Integer),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(42D, ExprEngine.Evaluate(
            "42",
            configuration: ExprConfiguration.Default.WithExpectedType(ExprTypes.Float),
            cancellationToken: TestContext.Current.CancellationToken));

        var environment = new Dictionary<string, object?> { ["rating"] = 5.5D };
        ExprConfiguration integerConfiguration = ExprConfiguration.Default
            .AllowUndefinedVariables()
            .WithExpectedType(ExprTypes.Integer);
        Assert.Equal(5L, ExprEngine.Evaluate(
            "rating",
            environment,
            integerConfiguration,
            cancellationToken: TestContext.Current.CancellationToken));

        ExprCheckException booleanError = Assert.Throws<ExprCheckException>(() => ExprEngine.Compile(
            "42",
            ExprConfiguration.Default.WithExpectedType(ExprTypes.Boolean)));
        Assert.Contains("expected bool", booleanError.Message, StringComparison.Ordinal);
        ExprCheckException floatError = Assert.Throws<ExprCheckException>(() => ExprEngine.Compile(
            "true",
            ExprConfiguration.Default.WithExpectedType(ExprTypes.Float)));
        Assert.Contains("expected float", floatError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreDns_compile_corpus_accepts_registered_host_functions()
    {
        ExprConfiguration configuration = ExprConfiguration.Default
            .DisableBuiltin("type")
            .WithFunction(Function("metadata", [ExprTypes.String], ExprTypes.String))
            .WithFunction(Function("type", [], ExprTypes.String))
            .WithFunction(Function("name", [], ExprTypes.String))
            .WithFunction(Function("client_ip", [], ExprTypes.String))
            .WithFunction(Function("incidr", [ExprTypes.String, ExprTypes.String], ExprTypes.Boolean));
        string[] expressions =
        [
            "metadata('geoip/city/name') == 'Exampleshire'",
            "(type() == 'A' && name() == 'example.com') || client_ip() == '1.2.3.4'",
            "name() matches '^abc\\\\..*\\\\.example\\\\.com\\\\.$'",
            "type() in ['A', 'AAAA']",
            "incidr(client_ip(), '192.168.0.0/16')",
            "incidr(client_ip(), '127.0.0.0/24')",
        ];

        foreach (string expression in expressions)
        {
            _ = ExprEngine.Compile(expression, configuration);
        }
    }

    [Fact]
    public void Constant_function_failures_and_registration_errors_are_structured()
    {
        var divide = new ExprFunction(
            "divide",
            [new ExprFunctionOverload([ExprTypes.Integer, ExprTypes.Integer], ExprTypes.Integer)],
            static arguments => (long)arguments[0]! / (long)arguments[1]!);
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithFunction(divide)
            .WithConstantFunction("divide");

        Exception failure = Assert.ThrowsAny<Exception>(() => ExprEngine.Compile("1 + divide(1, 0)", configuration));
        Assert.Contains("divide", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => ExprConfiguration.Default.WithConstantFunction("missing"));
    }

    [Fact]
    public void Function_overload_numeric_rules_and_nil_arguments_match_regressions()
    {
        object?[]? observed = null;
        var capture = new ExprFunction(
            "capture",
            [new ExprFunctionOverload([ExprTypes.Any], ExprTypes.String, isVariadic: true)],
            arguments =>
            {
                observed = arguments.ToArray();
                return $"{arguments.Length}:{(arguments[0] is null ? "nil" : "value")}";
            });
        var numeric = new ExprFunction(
            "numeric",
            [new ExprFunctionOverload([ExprTypes.Float], ExprTypes.Boolean)],
            static _ => true);
        ExprConfiguration configuration = ExprConfiguration.Default.WithFunction(capture).WithFunction(numeric);

        Assert.Equal("2:value", ExprEngine.Evaluate(
            "capture(1, nil)",
            configuration: configuration,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal([1L, null], observed);
        Assert.Equal("1:nil", ExprEngine.Evaluate(
            "capture(nil)",
            configuration: configuration,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.True((bool)ExprEngine.Evaluate(
            "numeric(1)",
            configuration: configuration,
            cancellationToken: TestContext.Current.CancellationToken)!);
        Assert.True((bool)ExprEngine.Evaluate(
            "numeric(1.5)",
            configuration: configuration,
            cancellationToken: TestContext.Current.CancellationToken)!);
        Assert.Throws<ExprCheckException>(() => ExprEngine.Compile("numeric('invalid')", configuration));
        Assert.Throws<ExprCheckException>(() => ExprEngine.Compile("numeric(true)", configuration));
    }

    [Fact]
    public void Variadic_format_function_receives_nil_without_coercion()
    {
        var format = new ExprFunction(
            "sprintf",
            [new ExprFunctionOverload([ExprTypes.String, ExprTypes.Any], ExprTypes.String, isVariadic: true)],
            static arguments => $"result: {arguments[1]} {(arguments[2] is null ? "<nil>" : arguments[2])}");
        ExprConfiguration configuration = ExprConfiguration.Default.WithFunction(format);

        Assert.Equal("result: 1 <nil>", ExprEngine.Evaluate(
            "sprintf('result: %v %v', 1, nil)",
            configuration: configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Undefined_boolean_result_uses_expected_type_default()
    {
        ExprConfiguration configuration = ExprConfiguration.Default
            .AllowUndefinedVariables()
            .WithExpectedType(ExprTypes.Boolean);

        object? value = ExprEngine.Evaluate(
            "missing",
            new Dictionary<string, object?>(),
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(false, value);
    }

    [Fact]
    public void Disassembly_retains_builtin_identity()
    {
        CompiledExpression expression = ExprEngine.Compile("concat(1..2, 3..4)");

        string disassembly = expression.Program.Disassemble();

        Assert.Contains("concat", disassembly, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A(A)")]
    [InlineData("$env.unknown(int())")]
    [InlineData("user.Profile?.Address ?? 'Unknown address'")]
    public void Invalid_dynamic_or_strict_member_regressions_fail_deterministically(string source)
    {
        Assert.ThrowsAny<Exception>(() => ExprEngine.Compile(source));
    }

    private static object? Evaluate(string source, IReadOnlyDictionary<string, object?> environment) =>
        ExprEngine.Evaluate(
            source,
            environment,
            DynamicConfiguration,
            cancellationToken: TestContext.Current.CancellationToken);

    private static ExprFunction Function(
        string name,
        IReadOnlyList<ExprTypeDescriptor> parameters,
        ExprTypeDescriptor result) =>
        new(name, [new ExprFunctionOverload(parameters, result)], static _ => null);
}
