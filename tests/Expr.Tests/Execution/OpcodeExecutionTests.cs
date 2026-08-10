using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Execution;

public sealed class OpcodeExecutionTests
{
    [Fact]
    public void Specialized_known_function_call_opcodes_execute_in_argument_order()
    {
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithFunction(Function("f0", 0))
            .WithFunction(Function("f1", 1))
            .WithFunction(Function("f2", 2))
            .WithFunction(Function("f3", 3))
            .WithFunction(Function("f4", 4));

        Assert.Equal(0L, Evaluate("f0()", configuration: configuration));
        Assert.Equal(1L, Evaluate("f1(1)", configuration: configuration));
        Assert.Equal(3L, Evaluate("f2(1, 2)", configuration: configuration));
        Assert.Equal(6L, Evaluate("f3(1, 2, 3)", configuration: configuration));
        Assert.Equal(10L, Evaluate("f4(1, 2, 3, 4)", configuration: configuration));
    }

    [Fact]
    public void Direct_fast_and_dynamic_call_opcodes_execute_delegates()
    {
        var increment = new Func<long, long>(static value => value + 1);
        ExprProgram fast = Program(
            [
                (ExprOpcode.OpPush, 0),
                (ExprOpcode.OpPush, 1),
                (ExprOpcode.OpCallFast, 1),
            ],
            [41L, increment]);
        ExprProgram dynamic = Program(
            [
                (ExprOpcode.OpPush, 0),
                (ExprOpcode.OpPush, 1),
                (ExprOpcode.OpCall, 1),
            ],
            [41L, increment]);

        Assert.Equal(42L, Run(fast));
        Assert.Equal(42L, Run(dynamic));
    }

    [Fact]
    public void Root_environment_loads_static_members_methods_and_the_environment_itself()
    {
        var environment = new RootEnvironment(40L);
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithEnvironment(ExprEnvironmentSchema.Reflect<RootEnvironment>());

        Assert.Equal(42L, Evaluate("Value + 2", environment, configuration));
        Assert.Equal("hello!", Evaluate("Echo('hello')", environment, configuration));
        Assert.Equal("values:1,2,3", Evaluate("Join('values:', 1, 2, 3)", environment, configuration));
        Assert.Same(environment, Evaluate("$env", environment, configuration));
    }

    [Fact]
    public void Dynamic_fetch_supports_negative_array_and_utf8_byte_indexes()
    {
        Assert.Equal(3L, Evaluate("[1, 2, 3][-1]"));
        ExprProgram stringFetch = Program(
            [
                (ExprOpcode.OpPush, 0),
                (ExprOpcode.OpPush, 1),
                (ExprOpcode.OpFetch, 0),
            ],
            ["é", 0L]);
        Assert.Equal((byte)0xC3, Run(stringFetch));
    }

    [Fact]
    public void Cast_opcodes_use_canonical_dotnet_result_types()
    {
        Assert.Equal(
            1D,
            Evaluate(
                "1",
                configuration: ExprConfiguration.Default
                    .WithOptimization(false)
                    .WithExpectedType(ExprTypes.Float)));
        Assert.Equal(
            1L,
            Evaluate(
                "1",
                configuration: ExprConfiguration.Default
                    .WithOptimization(false)
                    .WithExpectedType(ExprTypes.Integer)));
        ExprProgram booleanCast = Program(
            [(ExprOpcode.OpNil, 0), (ExprOpcode.OpCast, (int)ExprCastKind.Boolean)],
            []);
        Assert.False((bool)Run(booleanCast)!);
    }

    [Fact]
    public void Non_short_circuit_boolean_opcodes_execute()
    {
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithShortCircuit(false);

        Assert.False((bool)Evaluate("true && false", configuration: configuration)!);
        Assert.True((bool)Evaluate("false || true", configuration: configuration)!);
    }

    [Fact]
    public void Signed_minimum_modulo_negative_one_matches_go_without_overflow()
    {
        ExprProgram program = Program(
            [
                (ExprOpcode.OpPush, 0),
                (ExprOpcode.OpPush, 1),
                (ExprOpcode.OpModulo, 0),
            ],
            [long.MinValue, -1L]);

        Assert.Equal(0L, Run(program));
    }

    [Fact]
    public void Time_and_duration_arithmetic_matches_upstream_runtime_helpers()
    {
        var instant = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        TimeSpan duration = TimeSpan.FromHours(2);

        Assert.Equal(
            instant + duration,
            Run(BinaryProgram(instant, duration, ExprOpcode.OpAdd)));
        Assert.Equal(
            duration,
            Run(BinaryProgram(instant + duration, instant, ExprOpcode.OpSubtract)));
        Assert.Equal(
            duration + duration,
            Run(BinaryProgram(duration, 2L, ExprOpcode.OpMultiply)));
    }

    [Fact]
    public void Dynamic_regular_expression_and_constant_byte_expression_execute()
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["text"] = "abc123",
            ["pattern"] = "^[a-z]+[0-9]+$",
        };
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .AllowUndefinedVariables();

        Assert.True((bool)Evaluate("text matches pattern", environment, configuration)!);
    }

    [Fact]
    public void Empty_reduce_throws_the_compiler_generated_error_operand()
    {
        ExprProgram program = ExecutionTestCompiler.Compile("reduce([], #acc + #)");

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() => Run(program));

        Assert.Contains("reduce of empty array", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Safe_call_reports_memory_and_work_measurements()
    {
        var safe = new ExprFunction(
            "safe",
            [new ExprFunctionOverload([ExprTypes.Integer], ExprTypes.Integer)],
            safeInvoker: static arguments => new ExprInvocationResult(arguments[0], 7));
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithFunction(safe);
        ExprProgram program = ExecutionTestCompiler.Compile("safe(42)", configuration);

        ExprEvaluationResult result = ExprEvaluator.Shared.EvaluateDetailed(
            program,
            options: new ExprEvaluationOptions { MemoryBudget = 100 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42L, result.Value);
        Assert.Equal(7UL, result.MemoryUsed);
        Assert.True(result.WorkUsed > 0);
    }

    private static ExprFunction Function(string name, int arity)
    {
        ExprTypeDescriptor[] parameters = Enumerable.Repeat<ExprTypeDescriptor>(ExprTypes.Integer, arity).ToArray();
        return new ExprFunction(
            name,
            [new ExprFunctionOverload(parameters, ExprTypes.Integer)],
            static arguments => arguments.ToArray().Sum(static value => ExprValue.ToInt64(value)));
    }

    private static object? Evaluate(
        string source,
        object? environment = null,
        ExprConfiguration? configuration = null) =>
        Run(ExecutionTestCompiler.Compile(source, configuration), environment);

    private static object? Run(ExprProgram program, object? environment = null) =>
        ExprEvaluator.Shared.Evaluate(
            program,
            environment,
            cancellationToken: TestContext.Current.CancellationToken);

    private static ExprProgram Program(
        IReadOnlyList<(ExprOpcode Opcode, int Argument)> instructions,
        IReadOnlyList<object?> constants)
    {
        SyntaxTree tree = new SyntaxParser().Parse("0");
        return new ExprProgram(
            tree,
            instructions.Select(item => new ExprInstruction(
                item.Opcode,
                item.Argument,
                new SourceLocation(0, 1))),
            constants,
            [],
            0);
    }

    private static ExprProgram BinaryProgram(object? left, object? right, ExprOpcode opcode) =>
        Program(
            [(ExprOpcode.OpPush, 0), (ExprOpcode.OpPush, 1), (opcode, 0)],
            [left, right]);

    private sealed record RootEnvironment(long Value)
    {
        public string Echo(string value) => value + "!";

        public string Join(string prefix, params long[] values) =>
            prefix + string.Join(',', values);
    }
}
