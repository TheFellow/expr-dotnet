using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Expr.Checking;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Checking;

public sealed class CheckerTests
{
    private static readonly ExprChecker Checker = new();

    public static TheoryData<string, ExprTypeDescriptor> ValidExpressions => new()
    {
        { "nil == nil", ExprTypes.Boolean },
        { "true != nil", ExprTypes.Boolean },
        { "!true", ExprTypes.Boolean },
        { "1 + 2 * 3", ExprTypes.Integer },
        { "1 + 2.5", ExprTypes.Float },
        { "1 / 2", ExprTypes.Float },
        { "2 ** 3", ExprTypes.Float },
        { "'a' + 'b'", ExprTypes.String },
        { "'a' < 'b'", ExprTypes.Boolean },
        { "2 in 1..3", ExprTypes.Boolean },
        { "[1, 2.0]", ExprTypes.ArrayOf(ExprTypes.Float) },
        { "true ? 1 : 2.0", ExprTypes.Float },
        { "let answer = 42; answer + 1", ExprTypes.Integer },
        { "'abc' matches '^[a-z]+$'", ExprTypes.Boolean },
        { "len([1, 2, 3])", ExprTypes.Integer },
        { "map(1..3, {# + #index})", ExprTypes.ArrayOf(ExprTypes.Integer) },
        { "filter(1..3, {# > 1})", ExprTypes.ArrayOf(ExprTypes.Integer) },
        { "count(1..3, {# > 1})", ExprTypes.Integer },
    };

    [Theory]
    [MemberData(nameof(ValidExpressions))]
    public void Checker_ports_core_upstream_type_matrix(string expression, ExprTypeDescriptor expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ExprSemanticModel model = Check(expression);

        Assert.True(expected.IsEquivalentTo(model.ResultType), $"expected {expected}, got {model.ResultType}");
    }

    [Theory]
    [InlineData("unknown", "unknown name unknown")]
    [InlineData("1 + true", "invalid operation: + (mismatched types int and bool)")]
    [InlineData("1 and false", "invalid operation: and (mismatched types int and bool)")]
    [InlineData("42 in ['a']", "cannot use int as type string in array")]
    [InlineData("count(1, {#})", "builtin count takes only array (got int)")]
    [InlineData("count([1], {#})", "predicate should return boolean (got int)")]
    public void Checker_reports_first_upstream_compatible_diagnostic(string expression, string expected)
    {
        ExprCheckException exception = Assert.Throws<ExprCheckException>(() => Check(expression));

        Assert.StartsWith(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains("^", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Undefined_variables_are_any_when_strict_checking_is_disabled()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.AllowUndefinedVariables();

        ExprSemanticModel model = Check("missing + fn()", configuration);

        Assert.Same(ExprTypes.Any, model.ResultType);
    }

    [Fact]
    public void Environment_and_env_keyword_resolve_strict_schema_members()
    {
        _ = new TestEnvironment(0, string.Empty);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<TestEnvironment>()
            .Member("number", static environment => environment.Number)
            .Member("name", static environment => environment.Name)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        Assert.Same(ExprTypes.Integer, Check("number", configuration).ResultType);
        Assert.Same(ExprTypes.String, Check("$env['name']", configuration).ResultType);
        Assert.Throws<ExprCheckException>(() => Check("$env.missing", configuration));
        Assert.Same(ExprTypes.Any, Check("$env?.missing", configuration).ResultType);
    }

    [Fact]
    public void Strict_maps_validate_known_fields_and_index_types()
    {
        _ = new MapEnvironment(new Dictionary<string, object?>(StringComparer.Ordinal));
        var fields = new Dictionary<string, ExprTypeDescriptor>(StringComparer.Ordinal)
        {
            ["name"] = ExprTypes.String,
        };
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<MapEnvironment>()
            .Member("item", static environment => environment.Item, new MapTypeDescriptor(fields))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        Assert.Same(ExprTypes.String, Check("item.name", configuration).ResultType);
        Assert.Throws<ExprCheckException>(() => Check("item.missing", configuration));
        Assert.Throws<ExprCheckException>(() => Check("item[0]", configuration));
    }

    [Fact]
    public void Function_overloads_choose_most_specific_signature_and_validate_variadics()
    {
        var function = new ExprFunction(
            "add",
            [
                new ExprFunctionOverload([ExprTypes.Integer, ExprTypes.Integer], ExprTypes.Integer),
                new ExprFunctionOverload([ExprTypes.Float, ExprTypes.Float], ExprTypes.Float),
                new ExprFunctionOverload([ExprTypes.String], ExprTypes.String, isVariadic: true),
            ],
            static _ => null);
        ExprConfiguration configuration = ExprConfiguration.Default.WithFunction(function);

        Assert.Same(ExprTypes.Integer, Check("add(1, 2)", configuration).ResultType);
        Assert.Same(ExprTypes.Float, Check("add(1.0, 2)", configuration).ResultType);
        Assert.Same(ExprTypes.String, Check("add('a', 'b', 'c')", configuration).ResultType);
        Assert.Contains(
            "cannot use bool as argument",
            Assert.Throws<ExprCheckException>(() => Check("add(1, true)", configuration)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_and_host_overridden_builtins_are_honored_on_preparsed_trees()
    {
        ExprConfiguration disabled = ExprConfiguration.Default.DisableBuiltin("len");
        var len = new ExprFunction(
            "len",
            [new ExprFunctionOverload([ExprTypes.Integer], ExprTypes.String)],
            static _ => "host");
        ExprConfiguration overridden = ExprConfiguration.Default.WithFunction(len);

        Assert.Contains(
            "unknown builtin len",
            Assert.Throws<ExprCheckException>(() => Check("len([1])", disabled)).Message,
            StringComparison.Ordinal);
        Assert.Same(ExprTypes.String, Check("len(42)", overridden).ResultType);
    }

    [Fact]
    public void Expected_result_contract_can_accept_or_reject_any()
    {
        ExprConfiguration permissive = ExprConfiguration.Default
            .AllowUndefinedVariables()
            .WithExpectedType(ExprTypes.Boolean);
        ExprConfiguration warning = ExprConfiguration.Default
            .AllowUndefinedVariables()
            .WithExpectedType(ExprTypes.Boolean, warnOnAny: true);

        Assert.Same(ExprTypes.Any, Check("unknown", permissive).ResultType);
        Assert.Contains(
            "expected bool, but got any",
            Assert.Throws<ExprCheckException>(() => Check("unknown", warning)).Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("'x' matches '(?=x)x'")]
    [InlineData("'aa' matches `(a)\\1`")]
    public void Regex_literals_reject_features_outside_safe_non_backtracking_engine(string expression)
    {
        ExprCheckException exception = Assert.Throws<ExprCheckException>(() => Check(expression));

        Assert.Contains("regular expression", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Regex_literals_enforce_configured_length_limit()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithRegularExpressionLimits(
            TimeSpan.FromMilliseconds(50),
            3);

        ExprCheckException exception = Assert.Throws<ExprCheckException>(() =>
            Check("'test' matches 'test'", configuration));

        Assert.Contains("maximum length of 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Checker_depth_is_bounded_for_consumer_created_trees()
    {
        SyntaxNode root = new BooleanNode(true, default);
        for (var index = 0; index < 16; index++)
        {
            root = new UnaryNode("!", root, default);
        }

        var tree = new SyntaxTree(root, new SourceText(""));
        ExprConfiguration configuration = ExprConfiguration.Default.WithMaximumCheckDepth(8);

        ExprCheckException exception = Assert.Throws<ExprCheckException>(() => Checker.Check(tree, configuration));
        Assert.Contains("maximum checker depth", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Consumer_created_pointer_is_rejected_outside_predicate()
    {
        var pointer = new PointerNode(string.Empty, default);
        var tree = new SyntaxTree(pointer, new SourceText(string.Empty));

        ExprCheckException exception = Assert.Throws<ExprCheckException>(() => Checker.Check(tree));

        Assert.Contains("cannot use pointer accessor outside predicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Semantic_annotations_use_node_identity_without_mutating_equal_records()
    {
        var first = new IntegerNode(1, default);
        var second = new IntegerNode(1, default);
        var root = new ArrayNode([first, second], default);

        ExprSemanticModel model = Checker.Check(new SyntaxTree(root, new SourceText("[1, 1]")));

        Assert.Equal(3, model.Annotations.Count);
        Assert.True(model.TryGetSemantics(first, out ExprNodeSemantics? firstSemantics));
        Assert.True(model.TryGetSemantics(second, out ExprNodeSemantics? secondSemantics));
        Assert.NotSame(firstSemantics, secondSemantics);
        Assert.Same(first, root.Elements[0]);
        Assert.Same(second, root.Elements[1]);
    }

    [Fact]
    public void Try_check_returns_structured_diagnostic()
    {
        SyntaxTree tree = new SyntaxParser().Parse("1 + true");

        bool success = Checker.TryCheck(tree, out ExprSemanticModel? model, out ExprCheckDiagnostic? diagnostic);

        Assert.False(success);
        Assert.Null(model);
        Assert.NotNull(diagnostic);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(2, diagnostic.Column);
    }

    [Fact]
    [RequiresUnreferencedCode("Exercises reflection-backed nested CLR member checking.")]
    public void Clr_properties_and_overloaded_instance_methods_are_resolved_once()
    {
        _ = new ObjectEnvironment(new Person());
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ObjectEnvironment>()
            .Member("person", static environment => environment.Person)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        Assert.Same(ExprTypes.String, Check("person.Name", configuration).ResultType);
        Assert.Same(ExprTypes.Integer, Check("person.Score(2)", configuration).ResultType);
        Assert.Same(ExprTypes.Float, Check("person.Score(2.0)", configuration).ResultType);
        Assert.Throws<ExprCheckException>(() => Check("person.Missing", configuration));
    }

    [Fact]
    [RequiresUnreferencedCode("Exercises reflection-backed environment method checking.")]
    public void Environment_instance_methods_are_available_as_top_level_functions()
    {
        _ = new MethodEnvironment();
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<MethodEnvironment>().Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        Assert.Same(ExprTypes.Integer, Check("Double(21)", configuration).ResultType);
    }

    private static ExprSemanticModel Check(string expression, ExprConfiguration? configuration = null)
    {
        SyntaxTree tree = new SyntaxParser().Parse(expression);
        return Checker.Check(tree, configuration);
    }

    private sealed record TestEnvironment(long Number, string Name);

    private sealed record MapEnvironment(IReadOnlyDictionary<string, object?> Item);

    private sealed record ObjectEnvironment(Person Person);

    private sealed class Person
    {
        public string Name => "Ada";

        public int Score(int value) => value;

        public double Score(double value) => value;
    }

    private sealed class MethodEnvironment
    {
        public long Double(long value) => value * 2;
    }
}
