using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Security.Tests;

public sealed class EvaluationSecurityTests
{
    [Fact]
    public void End_to_end_memory_budget_rejects_range_at_the_exact_boundary()
    {
        CompiledExpression expression = ExprEngine.Compile("1..16");

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                options: new ExprEvaluationOptions { MemoryBudget = 16 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("memory budget exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void End_to_end_work_budget_stops_predicate_iteration()
    {
        CompiledExpression expression = ExprEngine.Compile("all(1..1000, # > 0)");

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                options: new ExprEvaluationOptions
                {
                    MemoryBudget = 0,
                    WorkBudget = 64,
                    MaximumCollectionLength = 2_000,
                },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("work budget exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("left + right")]
    [InlineData("left[0:32]")]
    public void Vm_string_allocations_honor_the_evaluation_memory_budget(string source)
    {
        var schema = new ExprEnvironmentSchemaBuilder<StringEnvironment>()
            .Member("left", static environment => environment.Left, ExprTypes.String)
            .Member("right", static environment => environment.Right, ExprTypes.String)
            .Build();
        CompiledExpression expression = ExprEngine.Compile(
            source,
            ExprConfiguration.Default.WithEnvironment(schema));
        var environment = new StringEnvironment(new string('a', 32), new string('b', 32));

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                environment,
                new ExprEvaluationOptions { MemoryBudget = 16 },
                TestContext.Current.CancellationToken));

        Assert.Contains("memory budget exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("replace(left, 'a', 'aa')")]
    [InlineData("upper(left)")]
    public void Allocating_string_builtins_report_cost_to_the_evaluation_budget(string source)
    {
        var schema = new ExprEnvironmentSchemaBuilder<StringEnvironment>()
            .Member("left", static environment => environment.Left, ExprTypes.String)
            .Member("right", static environment => environment.Right, ExprTypes.String)
            .Build();
        CompiledExpression expression = ExprEngine.Compile(
            source,
            ExprConfiguration.Default.WithEnvironment(schema));

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                new StringEnvironment(new string('a', 32), string.Empty),
                new ExprEvaluationOptions { MemoryBudget = 16 },
                TestContext.Current.CancellationToken));

        Assert.Contains("memory budget exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void End_to_end_evaluation_honors_precancellation()
    {
        CompiledExpression expression = ExprEngine.Compile("sum(1..1000)");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            expression.Run(cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Hostile_collection_is_rejected_by_length_before_indexing_or_enumerating()
    {
        var hostile = new HostileArray(17);
        var schema = new ExprEnvironmentSchemaBuilder<ArrayEnvironment>()
            .Member("items", static environment => environment.Items, ExprTypes.ArrayOf(ExprTypes.Any))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
        CompiledExpression expression = ExprEngine.Compile("all(items, true)", configuration);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                new ArrayEnvironment(hostile),
                new ExprEvaluationOptions { MaximumCollectionLength = 16 },
                TestContext.Current.CancellationToken));

        Assert.Contains("collection limit", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, hostile.IndexAttempts);
        Assert.Equal(0, hostile.EnumerationAttempts);
    }

    [Fact]
    public void Explicit_schema_and_member_attributes_bound_the_checked_member_surface()
    {
        var schema = new ExprEnvironmentSchemaBuilder<MemberEnvironment>()
            .Member(
                "payload",
                static environment => environment.Payload,
                new ObjectTypeDescriptor(typeof(MemberPayload)))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        Assert.Equal(
            "allowed",
            ExprEngine.Evaluate(
                "payload.Allowed",
                new MemberEnvironment(new MemberPayload()),
                configuration,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ExprCheckException>(() => ExprEngine.Compile("payload.Hidden", configuration));
        Assert.Throws<ExprCheckException>(() => ExprEngine.Compile("payload.GetType()", configuration));
    }

    [Fact]
    public void Any_typed_environment_value_cannot_discover_reflection_members()
    {
        var schema = new ExprEnvironmentSchemaBuilder<AnyEnvironment>()
            .Member("payload", static environment => environment.Payload, ExprTypes.Any)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
        CompiledExpression expression = ExprEngine.Compile("payload.Assembly.FullName", configuration);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                new AnyEnvironment(typeof(string)),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("cannot fetch", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(string).Assembly.FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_strict_object_environment_cannot_be_reflectively_discovered()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.AllowUndefinedVariables();
        CompiledExpression expression = ExprEngine.Compile("Secret", configuration);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                new NonStrictEnvironment(),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("cannot fetch", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NonStrictEnvironment.SecretValue, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reflection_metadata_is_rejected_by_checker_and_dynamic_builtins()
    {
        var typedSchema = new ExprEnvironmentSchemaBuilder<AnyEnvironment>()
            .Member(
                "payload",
                static environment => environment.Payload,
                new ObjectTypeDescriptor(typeof(Type)))
            .Build();
        Assert.Throws<ExprCheckException>(() =>
            ExprEngine.Compile(
                "payload.Assembly",
                ExprConfiguration.Default.WithEnvironment(typedSchema)));

        var anySchema = new ExprEnvironmentSchemaBuilder<AnyEnvironment>()
            .Member("payload", static environment => environment.Payload, ExprTypes.Any)
            .Build();
        ExprConfiguration anyConfiguration = ExprConfiguration.Default.WithEnvironment(anySchema);
        var environment = new AnyEnvironment(typeof(string));

        object? get = ExprEngine.Evaluate(
            "get(payload, 'Assembly')",
            environment,
            anyConfiguration,
            cancellationToken: TestContext.Current.CancellationToken);
        ExprExecutionException json = Assert.Throws<ExprExecutionException>(() =>
            ExprEngine.Evaluate(
                "toJSON(payload)",
                environment,
                anyConfiguration,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Null(get);
        Assert.Contains("unsupported value", json.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_strict_dictionary_environment_performs_keyed_lookup_only()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.AllowUndefinedVariables();
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["answer"] = 42L,
        };

        Assert.Equal(
            42L,
            ExprEngine.Evaluate(
                "answer",
                environment,
                configuration,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Any_typed_value_cannot_invoke_unallowlisted_methods_through_get()
    {
        var schema = new ExprEnvironmentSchemaBuilder<AnyEnvironment>()
            .Member("payload", static environment => environment.Payload, ExprTypes.Any)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
        CompiledExpression expression = ExprEngine.Compile(
            "let method = get(payload, 'Dangerous'); method()",
            configuration);
        var payload = new DangerousPayload();

        Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                new AnyEnvironment(payload),
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, payload.InvocationAttempts);
    }

    [Fact]
    public void Repeated_dynamic_get_misses_are_safe_and_side_effect_free()
    {
        var schema = new ExprEnvironmentSchemaBuilder<GetEnvironment>()
            .Member("payload", static environment => environment.Payload, ExprTypes.Any)
            .Member("key", static environment => environment.Key, ExprTypes.String)
            .Build();
        CompiledExpression expression = ExprEngine.Compile(
            "get(payload, key)",
            ExprConfiguration.Default.WithEnvironment(schema));
        var payload = new DangerousPayload();

        for (var index = 0; index < 1_000; index++)
        {
            object? value = expression.Run(
                new GetEnvironment(payload, string.Concat("missing-", index.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(value);
        }

        Assert.Equal(0, payload.InvocationAttempts);
    }

    [Fact]
    public void Dynamic_regular_expressions_reject_backreferences_and_length_abuse()
    {
        ExprConfiguration configuration = ExprConfiguration.Default
            .AllowUndefinedVariables()
            .WithOptimization(false);
        CompiledExpression expression = ExprEngine.Compile("text matches pattern", configuration);
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["text"] = new string('a', 1_000),
            ["pattern"] = "(a)\\1",
        };

        ExprExecutionException unsupported = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(environment, cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(unsupported.InnerException);

        environment["pattern"] = new string('a', 65);
        ExprExecutionException tooLong = Assert.Throws<ExprExecutionException>(() =>
            expression.Run(
                environment,
                new ExprEvaluationOptions { MaximumRegularExpressionLength = 64 },
                TestContext.Current.CancellationToken));
        Assert.Contains("maximum length", tooLong.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExprOpcode.OpPop, 0, "stack underflow")]
    [InlineData(ExprOpcode.OpJump, -1, "forward jump target")]
    [InlineData(ExprOpcode.OpJumpBackward, 0, "backward jump target")]
    [InlineData(ExprOpcode.OpTrue, 1, "unexpected operand")]
    [InlineData(ExprOpcode.OpCast, int.MaxValue, "cast operand")]
    [InlineData((ExprOpcode)255, 0, "unknown opcode")]
    public void Malformed_public_bytecode_is_rejected_deterministically(
        ExprOpcode opcode,
        int argument,
        string expected)
    {
        ExprProgram program = Program(new ExprInstruction(opcode, argument, new SourceLocation(0, 1)));

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_source_ranges_and_unclosed_scopes_are_rejected()
    {
        ExprProgram invalidLocation = Program(
            new ExprInstruction(ExprOpcode.OpInt, 1, new SourceLocation(0, 2)));
        ExprExecutionException location = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                invalidLocation,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("invalid source location", location.Message, StringComparison.Ordinal);

        ExprProgram openScope = Program(
            new ExprInstruction(ExprOpcode.OpPush, 0, new SourceLocation(0, 1)),
            new ExprInstruction(ExprOpcode.OpBegin, 0, new SourceLocation(0, 1)),
            new ExprInstruction(ExprOpcode.OpInt, 1, new SourceLocation(0, 1)),
            constants: [new ExprArray([1L])]);
        ExprExecutionException scope = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                openScope,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("open predicate scope", scope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stack_limit_is_enforced_before_operand_growth()
    {
        ExprProgram program = Program(
            new ExprInstruction(ExprOpcode.OpInt, 1, new SourceLocation(0, 1)),
            new ExprInstruction(ExprOpcode.OpInt, 2, new SourceLocation(0, 1)));

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                options: new ExprEvaluationOptions { MaximumStackDepth = 1 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("stack depth exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluation_diagnostics_do_not_invoke_or_disclose_host_stringification()
    {
        var hostile = new HostileDisplay();
        ExprProgram program = Program(
            [
                new ExprInstruction(ExprOpcode.OpPush, 0, new SourceLocation(0, 1)),
                new ExprInstruction(ExprOpcode.OpPush, 1, new SourceLocation(0, 1)),
                new ExprInstruction(ExprOpcode.OpFetch, 0, new SourceLocation(0, 1)),
            ],
            [1L, hostile]);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, hostile.StringificationAttempts);
        Assert.DoesNotContain(HostileDisplay.Secret, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(HostileDisplay).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytecode_disassembly_does_not_invoke_or_disclose_host_stringification()
    {
        var hostile = new HostileDisplay();
        ExprProgram program = Program(
            new ExprInstruction(ExprOpcode.OpPush, 0, new SourceLocation(0, 1)),
            constants: [hostile]);

        string disassembly = program.Disassemble();

        Assert.Equal(0, hostile.StringificationAttempts);
        Assert.DoesNotContain(HostileDisplay.Secret, disassembly, StringComparison.Ordinal);
        Assert.Contains(typeof(HostileDisplay).FullName!, disassembly, StringComparison.Ordinal);
    }

    private static ExprProgram Program(
        ExprInstruction instruction,
        params ExprInstruction[] instructions) =>
        Program([instruction, .. instructions], []);

    private static ExprProgram Program(
        ExprInstruction instruction,
        IReadOnlyList<object?> constants) =>
        Program([instruction], constants);

    private static ExprProgram Program(
        ExprInstruction first,
        ExprInstruction second,
        ExprInstruction third,
        IReadOnlyList<object?> constants) =>
        Program([first, second, third], constants);

    private static ExprProgram Program(
        IEnumerable<ExprInstruction> instructions,
        IReadOnlyList<object?> constants)
    {
        SyntaxTree tree = new SyntaxParser().Parse("0");
        return new ExprProgram(tree, instructions, constants, [], 0);
    }

    private sealed class HostileArray(int count) : IExprArray
    {
        public Type ElementType => typeof(object);

        public int Count { get; } = count;

        public int IndexAttempts { get; private set; }

        public int EnumerationAttempts { get; private set; }

        public object? this[int index]
        {
            get
            {
                IndexAttempts++;
                throw new InvalidOperationException("Index access was not authorized.");
            }
        }

        public IEnumerator<object?> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidOperationException("Enumeration was not authorized.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private readonly record struct ArrayEnvironment(IExprArray Items);

    private readonly record struct MemberEnvironment(MemberPayload Payload);

    private readonly record struct AnyEnvironment(object Payload);

    private readonly record struct GetEnvironment(object Payload, string Key);

    private readonly record struct StringEnvironment(string Left, string Right);

    private sealed class MemberPayload
    {
        public string Allowed => "allowed";

        [ExprMember(Ignore = true)]
        public string Hidden => "hidden";
    }

    private sealed class HostileDisplay
    {
        public const string Secret = "DO-NOT-DISCLOSE-HOSTILE-DISPLAY";

        public int StringificationAttempts { get; private set; }

        public override string ToString()
        {
            StringificationAttempts++;
            return Secret;
        }
    }

    private sealed class NonStrictEnvironment
    {
        public const string SecretValue = "DO-NOT-DISCOVER-NON-STRICT-ROOT";

        public string Secret => SecretValue;
    }

    private sealed class DangerousPayload
    {
        public int InvocationAttempts { get; private set; }

        public string Dangerous()
        {
            InvocationAttempts++;
            return "host-code-executed";
        }
    }
}
