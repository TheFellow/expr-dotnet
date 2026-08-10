using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Execution;

public sealed class ExecutionSecurityTests
{
    [Fact]
    public void Zero_memory_budget_means_unlimited()
    {
        ExprProgram program = ExecutionTestCompiler.Compile("1..100");

        ExprEvaluationResult result = ExprEvaluator.Shared.EvaluateDetailed(
            program,
            options: new ExprEvaluationOptions { MemoryBudget = 0 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(100, Assert.IsAssignableFrom<IExprArray>(result.Value).Count);
        Assert.Equal(100UL, result.MemoryUsed);
    }

    [Fact]
    public void Allocation_charges_enforce_the_upstream_memory_budget_boundary()
    {
        ExprProgram program = ExecutionTestCompiler.Compile("1..10");

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                options: new ExprEvaluationOptions { MemoryBudget = 10 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("memory budget exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_budget_stops_adversarial_backward_jumps()
    {
        ExprProgram program = Program([(ExprOpcode.OpJumpBackward, 1)]);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                options: new ExprEvaluationOptions { WorkBudget = 32 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("work budget exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_is_observed_during_backward_jumps()
    {
        ExprProgram program = Program([(ExprOpcode.OpJumpBackward, 1)]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ExprEvaluator.Shared.Evaluate(program, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Dynamic_regex_uses_nonbacktracking_engine_and_explicit_length_limit()
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["text"] = "aaaa",
            ["pattern"] = "(a)\\1",
        };
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .AllowUndefinedVariables();
        ExprProgram program = ExecutionTestCompiler.Compile("text matches pattern", configuration);

        ExprExecutionException unsupported = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                environment,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(unsupported.InnerException);

        environment["pattern"] = "abcdef";
        ExprExecutionException tooLong = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                environment,
                new ExprEvaluationOptions { MaximumRegularExpressionLength = 5 },
                TestContext.Current.CancellationToken));
        Assert.Contains("maximum length", tooLong.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Count")]
    [InlineData("Length")]
    public void Any_typed_array_and_string_values_reject_clr_member_fetches(string key)
    {
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["value"] = string.Equals(key, "Count", StringComparison.Ordinal)
                ? new ExprArray([1L, 2L])
                : "text",
            ["key"] = key,
        };
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .AllowUndefinedVariables();
        ExprProgram program = ExecutionTestCompiler.Compile("value[key]", configuration);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                environment,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("cannot fetch", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExprOpcode.OpPop, 0, "stack underflow")]
    [InlineData(ExprOpcode.OpEnd, 0, "predicate scope underflow")]
    [InlineData(ExprOpcode.OpJump, -1, "forward jump target")]
    [InlineData(ExprOpcode.OpJumpBackward, 0, "backward jump target")]
    [InlineData(ExprOpcode.OpCast, 100, "cast operand")]
    [InlineData(ExprOpcode.OpCreate, 100, "create operand")]
    [InlineData(ExprOpcode.OpTrue, 1, "unexpected operand")]
    public void Malformed_programs_are_rejected_deterministically(
        ExprOpcode opcode,
        int argument,
        string expected)
    {
        ExprProgram program = Program([(opcode, argument)]);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_constant_function_variable_and_unknown_opcode_operands_are_rejected()
    {
        AssertFailure(Program([(ExprOpcode.OpPush, 0)]), "constant operand");
        AssertFailure(Program([(ExprOpcode.OpLoadFunc, 0)]), "function operand");
        AssertFailure(Program([(ExprOpcode.OpLoadVar, 0)]), "local-variable operand");
        AssertFailure(Program([((ExprOpcode)255, 0)]), "unknown opcode");
        AssertFailure(Program([(ExprOpcode.OpInvalid, 0)]), "invalid opcode");
    }

    [Fact]
    public void Open_scope_and_unbalanced_profile_boundaries_are_rejected()
    {
        ExprProgram scopeProgram = Program(
            [(ExprOpcode.OpPush, 0), (ExprOpcode.OpBegin, 0), (ExprOpcode.OpInt, 1)],
            [new ExprArray([1L])]);
        AssertFailure(scopeProgram, "open predicate scope");

        var point = new ExprProfilePoint(0, null, "IntegerNode", new SourceLocation(0, 1));
        ExprProgram profileProgram = Program(
            [(ExprOpcode.OpProfileEnd, 0), (ExprOpcode.OpInt, 1)],
            [point]);
        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                profileProgram,
                options: new ExprEvaluationOptions { EnableProfiling = true },
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("profile scope is corrupt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stack_scope_and_collection_limits_are_enforced_before_growth()
    {
        ExprProgram stackProgram = Program(
            [(ExprOpcode.OpInt, 1), (ExprOpcode.OpInt, 2)]);
        ExprExecutionException stack = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                stackProgram,
                options: new ExprEvaluationOptions { MaximumStackDepth = 1 },
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("stack depth exceeded", stack.Message, StringComparison.Ordinal);

        ExprProgram collectionProgram = ExecutionTestCompiler.Compile("1..3");
        ExprExecutionException collection = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                collectionProgram,
                options: new ExprEvaluationOptions { MaximumCollectionLength = 2 },
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("collection limit", collection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Safe_function_resource_charge_is_enforced()
    {
        var expensive = new ExprFunction(
            "expensive",
            [new ExprFunctionOverload([], ExprTypes.Integer)],
            safeInvoker: static _ => new ExprInvocationResult(1L, 10));
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithOptimization(false)
            .WithFunction(expensive);
        ExprProgram program = ExecutionTestCompiler.Compile("expensive()", configuration);

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                options: new ExprEvaluationOptions { MemoryBudget = 10 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("memory budget exceeded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_failure_is_bound_to_the_emitting_source_location()
    {
        ExprProgram program = ExecutionTestCompiler.Compile("[1, 2][5]");

        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(exception.InstructionIndex >= 0);
        Assert.Contains("index out of range", exception.Message, StringComparison.Ordinal);
        Assert.Contains("(1:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("^", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_upstream_opcode_value_is_explicitly_represented()
    {
        ExprOpcode[] opcodes = Enum.GetValues<ExprOpcode>();

        Assert.Equal(84, opcodes.Length);
        Assert.Equal(ExprOpcode.OpInvalid, opcodes[0]);
        Assert.Equal(ExprOpcode.OpEnd, opcodes[^1]);
        Assert.Equal(Enumerable.Range(0, opcodes.Length), opcodes.Select(static opcode => (int)opcode));
    }

    private static void AssertFailure(ExprProgram program, string expected)
    {
        ExprExecutionException exception = Assert.Throws<ExprExecutionException>(() =>
            ExprEvaluator.Shared.Evaluate(
                program,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static ExprProgram Program(
        IReadOnlyList<(ExprOpcode Opcode, int Argument)> instructions,
        IReadOnlyList<object?>? constants = null)
    {
        SyntaxTree tree = new SyntaxParser().Parse("0");
        return new ExprProgram(
            tree,
            instructions.Select(item => new ExprInstruction(
                item.Opcode,
                item.Argument,
                new SourceLocation(0, 1))),
            constants ?? [],
            [],
            0);
    }
}
