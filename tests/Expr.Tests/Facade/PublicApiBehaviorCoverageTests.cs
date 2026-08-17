using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Optimization;
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
    [InlineData("any(values, # == 2) or any(values, # == 3)")]
    [InlineData("none(values, # < 0) and none(values, # > 10)")]
    [InlineData("all([nil, 1.5, true, 'x', b'x', -1, 1 + 2, [1], {'a': 1}], true) and all([nil, 1.5, true, 'x', b'x', -1, 1 + 2, [1], {'a': 1}], true)")]
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
    public void Checked_models_cannot_be_reinterpreted_under_a_different_configuration()
    {
        ExprConfiguration checkedConfiguration = ExprConfiguration.Default.WithOptimization(false);
        ExprConfiguration differentConfiguration = ExprConfiguration.Default.WithOptimization(true);
        ExprSemanticModel model = ExprEngine.Check("1 + 2", checkedConfiguration);

        Assert.Same(checkedConfiguration, model.Configuration);
        Assert.Throws<ArgumentException>(() => ExprOptimizer.Optimize(model, differentConfiguration));
        Assert.Throws<ArgumentException>(() => ExprCompiler.Compile(model, differentConfiguration));

        Assert.Throws<ArgumentNullException>(() => new ExprMemberBinding(null!, ExprMemberBindingKind.Index));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExprMemberBinding("value", (ExprMemberBindingKind)42));
        Assert.Throws<ArgumentException>(() => new ExprMemberBinding("value", ExprMemberBindingKind.ClrMember));
        Assert.Throws<ArgumentNullException>(() => new ExprValueConversionBinding(null!));
        Assert.Throws<ArgumentNullException>(() => new ExprNodeSemantics(null!));
        Assert.Throws<ArgumentException>(() => new ExprNodeSemantics(
            ExprTypes.Any,
            overload: new ExprFunctionOverload([], ExprTypes.Any)));
        var function = new ExprFunction(
            "function",
            [new ExprFunctionOverload([], ExprTypes.Integer)],
            static _ => 1L);
        Assert.Throws<ArgumentException>(() => new ExprNodeSemantics(
            ExprTypes.Any,
            function,
            new ExprFunctionOverload([], ExprTypes.String)));
    }

    [Fact]
    public void Evaluation_options_reject_invalid_state_at_construction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExprEvaluationOptions { WorkBudget = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExprEvaluationOptions { MaximumStackDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExprEvaluationOptions { MaximumScopeDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExprEvaluationOptions { MaximumCollectionLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExprEvaluationOptions { MaximumRegularExpressionLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExprEvaluationOptions { RegularExpressionTimeout = TimeSpan.Zero });
    }

    [Theory]
    [InlineData("!1", "invalid operation")]
    [InlineData("-'text'", "invalid operation")]
    [InlineData("[1].field", "array elements can only be selected using an integer")]
    [InlineData("[1]['from':]", "non-integer slice index")]
    [InlineData("[1][:'to']", "non-integer slice index")]
    [InlineData("sortBy([1], #, 1)", "order argument must be a string")]
    [InlineData("get([1], true)", "non-integer slice index bool")]
    [InlineData("let len = 1; len", "cannot redeclare builtin")]
    [InlineData("if 1 { 2 } else { 3 }", "non-bool expression")]
    public void Public_checker_rejects_invalid_consumer_expressions(string source, string message)
    {
        ExprCheckException exception = Assert.Throws<ExprCheckException>(() => ExprEngine.Check(source));

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_syntax_types_reject_structurally_invalid_state()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceLocation(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceLocation(2, 1));
        Assert.Throws<ArgumentNullException>(() => new SyntaxTree(null!, new SourceText(string.Empty)));
        Assert.Throws<ArgumentNullException>(() => new SyntaxTree(new NilNode(default), null!));
        Assert.Throws<ArgumentException>(() => new IdentifierNode(string.Empty, default));
        Assert.Throws<ArgumentException>(() => new SequenceNode([], default));
        Assert.Throws<ArgumentException>(() => new ArrayNode([null!], default));
        Assert.Throws<ArgumentNullException>(() => new CallNode(null!, [], default));
        Assert.Throws<ArgumentException>(() => new BuiltinNode(string.Empty, [], default));

        var binary = new BinaryNode("+", new IntegerNode(1, default), new IntegerNode(2, default), default);
        Assert.Throws<ArgumentNullException>(() => binary with { Left = null! });

        SyntaxNode value = new IntegerNode(1, default);
        Action[] invalidNodes =
        [
            () => _ = new StringNode(null!, default),
            () => _ = new UnaryNode(string.Empty, value, default),
            () => _ = new UnaryNode("-", null!, default),
            () => _ = new BinaryNode(string.Empty, value, value, default),
            () => _ = new BinaryNode("+", null!, value, default),
            () => _ = new BinaryNode("+", value, null!, default),
            () => _ = new ChainNode(null!, default),
            () => _ = new MemberNode(null!, value, false, false, default),
            () => _ = new MemberNode(value, null!, false, false, default),
            () => _ = new SliceNode(null!, null, null, default),
            () => _ = new CallNode(value, [null!], default),
            () => _ = new BuiltinNode("len", [null!], default),
            () => _ = new PredicateNode(null!, default),
            () => _ = new PointerNode(null!, default),
            () => _ = new ConditionalNode(null!, value, value, false, default),
            () => _ = new ConditionalNode(value, null!, value, false, default),
            () => _ = new ConditionalNode(value, value, null!, false, default),
            () => _ = new VariableDeclaratorNode(string.Empty, value, value, default),
            () => _ = new VariableDeclaratorNode("x", null!, value, default),
            () => _ = new VariableDeclaratorNode("x", value, null!, default),
            () => _ = new MapNode([null!], default),
            () => _ = new PairNode(null!, value, default),
            () => _ = new PairNode(value, null!, default),
        ];

        foreach (Action construct in invalidNodes)
        {
            Assert.ThrowsAny<ArgumentException>(construct);
        }

        Assert.Throws<ArgumentException>(() => binary with { Operator = string.Empty });
        Assert.Throws<ArgumentNullException>(() => binary with { Right = null! });

        var identifier = new IdentifierNode("value", default);
        var text = new StringNode("value", default);
        var unary = new UnaryNode("-", value, default);
        var chain = new ChainNode(value, default);
        var member = new MemberNode(value, value, false, false, default);
        var slice = new SliceNode(value, null, null, default);
        var predicate = new PredicateNode(value, default);
        var pointer = new PointerNode(string.Empty, default);
        var conditional = new ConditionalNode(value, value, value, false, default);
        var variable = new VariableDeclaratorNode("x", value, value, default);
        var pair = new PairNode(value, value, default);
        Action[] invalidMutations =
        [
            () => _ = identifier with { Name = string.Empty },
            () => _ = text with { Value = null! },
            () => _ = unary with { Operator = string.Empty },
            () => _ = unary with { Operand = null! },
            () => _ = chain with { Expression = null! },
            () => _ = member with { Target = null! },
            () => _ = member with { Property = null! },
            () => _ = slice with { Target = null! },
            () => _ = predicate with { Body = null! },
            () => _ = pointer with { Name = null! },
            () => _ = conditional with { Condition = null! },
            () => _ = conditional with { WhenTrue = null! },
            () => _ = conditional with { WhenFalse = null! },
            () => _ = variable with { Name = string.Empty },
            () => _ = variable with { Value = null! },
            () => _ = variable with { Body = null! },
            () => _ = pair with { Key = null! },
            () => _ = pair with { Value = null! },
        ];
        foreach (Action mutate in invalidMutations)
        {
            Assert.ThrowsAny<ArgumentException>(mutate);
        }
    }

    [Fact]
    public void Public_checker_rejects_semantically_invalid_consumer_trees()
    {
        var unsupported = new SyntaxTree(new UnsupportedNode(default), new SourceText(string.Empty));
        var nilMember = new SyntaxTree(
            new MemberNode(new NilNode(default), new StringNode("field", default), false, false, default),
            new SourceText(string.Empty));
        var nonCallable = new SyntaxTree(
            new CallNode(new IntegerNode(42, default), [], default),
            new SourceText(string.Empty));
        var standalonePair = new SyntaxTree(
            new PairNode(new StringNode("key", default), new IntegerNode(1, default), default),
            new SourceText(string.Empty));

        Assert.Contains(
            "undefined syntax node type",
            Assert.Throws<ExprCheckException>(() => ExprEngine.Check(unsupported)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "type nil has no field",
            Assert.Throws<ExprCheckException>(() => ExprEngine.Check(nilMember)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "not callable",
            Assert.Throws<ExprCheckException>(() => ExprEngine.Check(nonCallable)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "outside a map",
            Assert.Throws<ExprCheckException>(() => ExprEngine.Check(standalonePair)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Public_checker_covers_consumer_authored_ast_and_try_contracts()
    {
        var checker = new ExprChecker();
        SyntaxTree valid = new SyntaxParser().Parse("1 + 2");

        Assert.True(checker.TryCheck(valid, out ExprSemanticModel? model, out ExprCheckDiagnostic? diagnostic));
        Assert.NotNull(model);
        Assert.Null(diagnostic);

        SyntaxNode value = new IntegerNode(1, default);
        SyntaxNode[] invalidRoots =
        [
            new UnaryNode("unknown", value, default),
            new BinaryNode("unknown", value, value, default),
            new BuiltinNode("unknown", [value], default),
            new BuiltinNode("all", [], default),
            new BuiltinNode("any", [new ArrayNode([value], default)], default),
            new PointerNode(string.Empty, default),
        ];
        foreach (SyntaxNode root in invalidRoots)
        {
            var tree = new SyntaxTree(root, new SourceText(string.Empty));
            Assert.False(checker.TryCheck(tree, out model, out diagnostic));
            Assert.Null(model);
            Assert.NotNull(diagnostic);
        }
    }

    [Theory]
    [InlineData("$env()", "not callable")]
    [InlineData("get(1)", "expected 2")]
    [InlineData("len()", "invalid number of arguments")]
    [InlineData("abs(1, 2)", "invalid number of arguments")]
    [InlineData("let x = 1; let x = 2; x", "cannot redeclare variable")]
    public void Public_checker_rejects_reachable_contract_violations(string source, string message)
    {
        ExprCheckException exception = Assert.Throws<ExprCheckException>(() => ExprEngine.Check(source));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_checker_statically_types_temporal_operations_and_function_values()
    {
        var environment = new ContractEnvironment(
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromSeconds(2),
            new Func<long, long>(static value => value));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ContractEnvironment>()
            .Member("instant", static value => value.Instant, ExprTypes.Time)
            .Member("duration", static value => value.Duration, ExprTypes.Duration)
            .Member(
                "function",
                static value => value.Function,
                new FunctionTypeDescriptor([ExprTypes.Integer], ExprTypes.Integer))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        Assert.Same(ExprTypes.Time, ExprEngine.Check("instant + duration", configuration).ResultType);
        Assert.Same(ExprTypes.Time, ExprEngine.Check("duration + instant", configuration).ResultType);
        Assert.Same(ExprTypes.Duration, ExprEngine.Check("instant - instant", configuration).ResultType);
        Assert.Same(ExprTypes.Duration, ExprEngine.Check("duration - duration", configuration).ResultType);
        Assert.Same(ExprTypes.Duration, ExprEngine.Check("duration * 2", configuration).ResultType);
        Assert.Same(ExprTypes.Integer, ExprEngine.Check("function(1)", configuration).ResultType);
        Assert.Contains("not enough arguments", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("function()", configuration)).Message, StringComparison.Ordinal);
        Assert.Contains("too many arguments", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("function(1, 2)", configuration)).Message, StringComparison.Ordinal);
        Assert.Contains("cannot use bool", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("function(true)", configuration)).Message, StringComparison.Ordinal);

        Assert.Equal(2L, ExprEngine.Evaluate(
            "function(2)",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Public_checker_covers_strict_host_map_function_and_method_contracts()
    {
        _ = new CheckerEnvironment();
        var mapType = new MapTypeDescriptor(
            [new KeyValuePair<string, ExprTypeDescriptor>("known", ExprTypes.Integer)]);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<CheckerEnvironment>()
            .Member("map", static value => value.Map, mapType)
            .Member("number", static value => value.Number, ExprTypes.Integer)
            .Build();
        var hostFunction = new ExprFunction(
            "hostFunction",
            [new ExprFunctionOverload([], ExprTypes.Integer)],
            static _ => 1L);
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithFunction(hostFunction);

        Assert.IsType<FunctionTypeDescriptor>(ExprEngine.Check("len", configuration).ResultType);
        Assert.Same(ExprTypes.Any, ExprEngine.Check("map?.missing", configuration).ResultType);
        Assert.Contains("cannot use bool", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("get(map, true)", configuration)).Message, StringComparison.Ordinal);
        Assert.Contains("has no field", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("number.field", configuration)).Message, StringComparison.Ordinal);
        Assert.Contains("cannot redeclare number", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("let number = 1; number", configuration)).Message, StringComparison.Ordinal);
        Assert.Contains("cannot redeclare function", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("let hostFunction = 1; hostFunction", configuration)).Message, StringComparison.Ordinal);
        Assert.Contains("doesn't return value", Assert.Throws<ExprCheckException>(
            () => ExprEngine.Check("Void()", configuration)).Message, StringComparison.Ordinal);

        var invalid = new ExprFunction(
            "validate",
            [],
            static _ => null,
            typeValidator: static _ => throw new ArgumentException("host type contract failed"));
        ExprCheckException validation = Assert.Throws<ExprCheckException>(() =>
            ExprEngine.Check("validate(1)", ExprConfiguration.Default.WithFunction(invalid)));
        Assert.Contains("host type contract failed", validation.Message, StringComparison.Ordinal);
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

    [Fact]
    public void Public_dynamic_evaluation_exercises_runtime_only_host_shapes()
    {
        Assert.Equal(false, Evaluate("value matches 'x'", ("value", null)));
        Assert.Equal(
            TimeSpan.FromSeconds(2).Ticks * 2.5,
            Evaluate("duration * factor", ("duration", TimeSpan.FromSeconds(2)), ("factor", 2.5)));
        Assert.Equal(
            true,
            Evaluate(
                "all(value, true)",
                ("value", new Dictionary<string, object?> { ["one"] = 1L })));

        AssertRuntimeError("value['key']", "cannot fetch", ("value", "bytes"u8.ToArray()));
        AssertRuntimeError("value[0:1]", "invalid argument for len", ("value", 42L));
        AssertRuntimeError("value matches pattern", "Invalid pattern", ("value", "text"), ("pattern", "("));
        AssertRuntimeError("sortBy([1], #, order)", "unknown order", ("order", "sideways"));
        AssertRuntimeError("all(value, true)", "cannot iterate", ("value", 42L));

        var nested = ExprEngine.Compile(
            "all(values, all(#, true))",
            DynamicConfiguration);
        ExprExecutionException scope = Assert.Throws<ExprExecutionException>(() => nested.Run(
            new Dictionary<string, object?> { ["values"] = new object?[] { new object?[] { 1L } } },
            new ExprEvaluationOptions { MaximumScopeDepth = 1 },
            TestContext.Current.CancellationToken));
        Assert.Contains("scope depth", scope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_runtime_covers_compiler_generated_collection_profile_and_map_paths()
    {
        byte[] bytes = "hello"u8.ToArray();
        Assert.Equal(true, Evaluate("value matches 'ell'", ("value", bytes)));
        Assert.Equal(true, Evaluate("value matches 'ell'", ("value", new ReadOnlyMemory<byte>(bytes))));
        Assert.Equal(true, Evaluate("all(value, # > 0)", ("value", bytes)));
        Assert.Equal(true, Evaluate("all(value, # > 0)", ("value", "hello")));
        Assert.Equal(
            TimeSpan.FromSeconds(2).Ticks * 2.5,
            Evaluate("factor * duration", ("duration", TimeSpan.FromSeconds(2)), ("factor", 2.5)));

        CompiledExpression array = ExprEngine.Compile(
            "[1, 2]",
            ExprConfiguration.Default.WithOptimization(false));
        Assert.Contains("collection size", Assert.Throws<ExprExecutionException>(() => array.Run(
            options: new ExprEvaluationOptions { MaximumCollectionLength = 1 },
            cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);

        CompiledExpression profiled = ExprEngine.Compile(
            "1 + 2",
            compilationOptions: new ExprCompilationOptions { EnableProfiling = true });
        ExprEvaluationResult unmeasured = profiled.RunDetailed(
            options: new ExprEvaluationOptions { EnableProfiling = false },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(unmeasured.Profile);

        AssertRuntimeError(
            "all(value, # != nil)",
            "cannot index map predicate source",
            ("value", new Dictionary<string, object?> { ["x"] = 1L }));

        IExprMap groups = Assert.IsAssignableFrom<IExprMap>(Evaluate(
            "groupBy(values, # % 2)",
            ("values", new object?[] { 1L, 2L, 3L })));
        Assert.False(groups.TryGetValue(4L, out object? missing));
        Assert.Null(missing);
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, ((System.Collections.IEnumerable)groups).Cast<object>().Count());
    }

    [Fact]
    public void Public_dynamic_calls_adapt_real_host_delegate_parameters()
    {
        Assert.Equal(2L, Evaluate(
            "function(2)",
            ("function", new Func<ContractEnum, long>(static value => (long)value))));
        Assert.Equal(0, Evaluate(
            "function(nil)",
            ("function", new Func<int, int>(static value => value))));
        Assert.Equal(DateTime.UnixEpoch.Ticks, Evaluate(
            "function(value)",
            ("function", new Func<DateTime, long>(static value => value.Ticks)),
            ("value", DateTimeOffset.UnixEpoch)));
        Assert.Equal(DateTime.UnixEpoch.Ticks, Evaluate(
            "function(value)",
            ("function", new Func<DateTimeOffset, long>(static value => value.Ticks)),
            ("value", DateTime.UnixEpoch)));
        AssertRuntimeError(
            "function(value)",
            "cannot use",
            ("function", new Func<Uri, string>(static value => value.ToString())),
            ("value", new object()));
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

    private sealed record ContractEnvironment(
        DateTimeOffset Instant,
        TimeSpan Duration,
        Func<long, long> Function);

    private enum ContractEnum
    {
        Zero,
        One,
        Two,
    }

    private sealed class CheckerEnvironment
    {
        public IReadOnlyDictionary<string, object?> Map { get; } = new Dictionary<string, object?>();

        public long Number => 1;

        public void Void()
        {
        }
    }

    private abstract class BaseValue;

    private sealed class DerivedValue : BaseValue;

    private sealed record UnsupportedNode(SourceLocation Location) : SyntaxNode(Location);
}
