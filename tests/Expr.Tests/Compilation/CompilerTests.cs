using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Patching;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Compilation;

public sealed class CompilerTests
{
    public static TheoryData<string, ExprOpcode[]> CorePrograms => new()
    {
        { "65535", [ExprOpcode.OpPush] },
        { ".5", [ExprOpcode.OpPush] },
        { "true", [ExprOpcode.OpTrue] },
        { "'string' == 'string'", [ExprOpcode.OpPush, ExprOpcode.OpPush, ExprOpcode.OpEqualString] },
        { "1000000 == 1000000", [ExprOpcode.OpPush, ExprOpcode.OpPush, ExprOpcode.OpEqualInt] },
        { "-1", [ExprOpcode.OpPush, ExprOpcode.OpNegate] },
        {
            "true && true || true",
            [
                ExprOpcode.OpTrue, ExprOpcode.OpJumpIfFalse, ExprOpcode.OpPop, ExprOpcode.OpTrue,
                ExprOpcode.OpJumpIfTrue, ExprOpcode.OpPop, ExprOpcode.OpTrue,
            ]
        },
        {
            "1; 2; 3",
            [ExprOpcode.OpPush, ExprOpcode.OpPop, ExprOpcode.OpPush, ExprOpcode.OpPop, ExprOpcode.OpPush]
        },
        {
            "true ? 1 : 2",
            [
                ExprOpcode.OpTrue, ExprOpcode.OpJumpIfFalse, ExprOpcode.OpPop, ExprOpcode.OpPush,
                ExprOpcode.OpJump, ExprOpcode.OpPop, ExprOpcode.OpPush,
            ]
        },
    };

    // Provenance: inspiration/expr/compiler/compiler_test.go TestCompile.
    [Theory]
    [MemberData(nameof(CorePrograms))]
    public void Compiler_ports_core_upstream_instruction_sequences(string source, ExprOpcode[] expected)
    {
        ExprProgram program = Compile(source, ExprConfiguration.Default.WithOptimization(false));

        Assert.Equal(expected, program.Bytecode);
        Assert.Equal(program.Bytecode.Count, program.Arguments.Count);
        Assert.Equal(program.Bytecode.Count, program.Locations.Count);
        if (string.Equals(source, "true && true || true", StringComparison.Ordinal))
        {
            Assert.Equal([0, 2, 0, 0, 2, 0, 0], program.Arguments);
        }
    }

    [Fact]
    public void Logical_short_circuit_can_be_disabled()
    {
        ExprProgram program = Compile(
            "true && false || true",
            ExprConfiguration.Default.WithOptimization(false).WithShortCircuit(false));

        Assert.Equal(
            [ExprOpcode.OpTrue, ExprOpcode.OpFalse, ExprOpcode.OpAnd, ExprOpcode.OpTrue, ExprOpcode.OpOr],
            program.Bytecode);
    }

    [Fact]
    public void Compiler_deduplicates_scalar_constants_and_resolves_all_jumps()
    {
        ExprProgram program = Compile(
            "let foo = true; let bar = false; let baz = true; foo || bar || baz",
            ExprConfiguration.Default.WithOptimization(true));

        Assert.Equal(3, program.VariableCount);
        Assert.Equal("foo", program.VariableNames[0]);
        Assert.Equal("bar", program.VariableNames[1]);
        Assert.Equal("baz", program.VariableNames[2]);
        Assert.DoesNotContain(program.Arguments, static argument => argument == 12_345);
        Assert.Equal(5, program.Arguments[7]);
        Assert.Equal(2, program.Arguments[10]);
    }

    [Fact]
    public void Arrays_maps_ranges_slices_and_constant_regexes_lower_to_dedicated_opcodes()
    {
        ExprProgram program = Compile(
            "[{'a': (1..3)[0:2]}, {'b': 'abc' matches '^[a-z]+$'}]",
            ExprConfiguration.Default.WithOptimization(false));

        Assert.Contains(ExprOpcode.OpRange, program.Bytecode);
        Assert.Contains(ExprOpcode.OpSlice, program.Bytecode);
        Assert.Contains(ExprOpcode.OpMap, program.Bytecode);
        Assert.Contains(ExprOpcode.OpArray, program.Bytecode);
        Assert.Contains(ExprOpcode.OpMatchesConst, program.Bytecode);
        Assert.Contains(program.Constants, static value => value is ExprRegularExpressionOperand);
    }

    [Fact]
    public void Constant_regex_limits_are_enforced_during_checking()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.WithRegularExpressionLimits(
            TimeSpan.FromMilliseconds(100),
            3);
        SyntaxTree tree = new SyntaxParser().Parse("'text' matches 'long'");
        ExprCheckException exception = Assert.Throws<ExprCheckException>(
            () => new ExprChecker().Check(tree, configuration));

        Assert.Contains("maximum length", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expected_result_contract_emits_the_canonical_dotnet_cast()
    {
        ExprProgram program = Compile(
            "1",
            ExprConfiguration.Default.WithExpectedType(ExprTypes.Integer).WithOptimization(false));

        Assert.Equal(ExprOpcode.OpCast, program.Bytecode[^1]);
        Assert.Equal((int)ExprCastKind.Integer64, program.Arguments[^1]);
    }

    [Fact]
    public void Known_functions_use_specialized_calls_and_immutable_debug_tables()
    {
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithFunction(Function("f0", 0))
            .WithFunction(Function("f1", 1))
            .WithFunction(Function("f2", 2))
            .WithFunction(Function("f3", 3))
            .WithFunction(Function("f4", 4));

        ExprProgram program = Compile("f0(); f1(1); f2(1, 2); f3(1, 2, 3); f4(1, 2, 3, 4)", configuration);

        Assert.Contains(ExprOpcode.OpCall0, program.Bytecode);
        Assert.Contains(ExprOpcode.OpCall1, program.Bytecode);
        Assert.Contains(ExprOpcode.OpCall2, program.Bytecode);
        Assert.Contains(ExprOpcode.OpCall3, program.Bytecode);
        Assert.Contains(ExprOpcode.OpLoadFunc, program.Bytecode);
        Assert.Contains(ExprOpcode.OpCallN, program.Bytecode);
        Assert.Equal(["f0", "f1", "f2", "f3", "f4"], program.FunctionNames.Values);
    }

    [Fact]
    public void Safe_functions_lower_to_resource_accounting_call()
    {
        var safe = new ExprFunction(
            "safe",
            [new ExprFunctionOverload([ExprTypes.Integer], ExprTypes.Integer)],
            safeInvoker: static arguments => new ExprInvocationResult(arguments[0], 1));
        ExprConfiguration configuration = ExprConfiguration.Default.WithFunction(safe);

        ExprProgram program = Compile("safe(1)", configuration);

        Assert.Equal([ExprOpcode.OpPush, ExprOpcode.OpLoadFunc, ExprOpcode.OpCallSafe], program.Bytecode);
    }

    [Fact]
    public void Static_environment_members_and_clr_methods_retain_checker_bindings()
    {
        _ = new HostEnvironment(new HostValue("value"));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<HostEnvironment>()
            .Member("host", static environment => environment.Host)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        ExprProgram program = Compile("host.Echo('hello')", configuration);

        Assert.Equal(
            [ExprOpcode.OpPush, ExprOpcode.OpLoadField, ExprOpcode.OpMethod, ExprOpcode.OpCallTyped],
            program.Bytecode);
        ExprMemberOperand[] members = [.. program.Constants.OfType<ExprMemberOperand>()];
        Assert.Equal(2, members.Length);
        Assert.Equal(ExprMemberBindingKind.Environment, members[0].Kind);
        Assert.NotNull(members[0].EnvironmentMember);
        Assert.Equal(ExprMemberBindingKind.ClrMethod, members[1].Kind);
        Assert.NotNull(members[1].Member);
    }

    [Fact]
    public void String_keyed_any_environment_uses_upstream_fast_load()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["answer"] = 42L };
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(ExprEnvironmentSchema.FromDictionary(values));

        ExprProgram program = Compile("answer", configuration);

        Assert.Equal([ExprOpcode.OpLoadFast], program.Bytecode);
        Assert.Equal("answer", program.Constants[0]);
    }

    [Fact]
    public void Value_provider_bindings_emit_dereference_before_use()
    {
        _ = new ProviderEnvironment(new IntegerProvider(41));
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ProviderEnvironment>()
            .Member("wrapped", static environment => environment.Wrapped)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithValueProviders();

        ExprProgram program = Compile("wrapped + 1", configuration);

        Assert.Equal(
            [ExprOpcode.OpLoadField, ExprOpcode.OpDeref, ExprOpcode.OpPush, ExprOpcode.OpAdd],
            program.Bytecode);
    }

    [Fact]
    public void Profiling_emits_balanced_boundaries_with_parented_source_points()
    {
        const string source = "1 + 2";
        SyntaxTree tree = new SyntaxParser().Parse(source);
        ExprConfiguration configuration = ExprConfiguration.Default.WithOptimization(false);
        ExprSemanticModel model = new ExprChecker().Check(tree, configuration);

        ExprProgram program = ExprCompiler.Compile(
            model,
            configuration,
            new ExprCompilationOptions { EnableProfiling = true });

        Assert.Equal(3, program.ProfilePoints.Count);
        Assert.Null(program.ProfilePoints[0].ParentId);
        Assert.Equal(0, program.ProfilePoints[1].ParentId);
        Assert.Equal(0, program.ProfilePoints[2].ParentId);
        Assert.Equal(3, program.Bytecode.Count(static opcode => opcode is ExprOpcode.OpProfileStart));
        Assert.Equal(3, program.Bytecode.Count(static opcode => opcode is ExprOpcode.OpProfileEnd));
        foreach (ExprProfilePoint point in program.ProfilePoints)
        {
            int constantIndex = Assert.Single(
                Enumerable.Range(0, program.Constants.Count),
                index => ReferenceEquals(program.Constants[index], point));
            Assert.Contains(
                program.Instructions,
                instruction => instruction.Opcode is ExprOpcode.OpProfileStart &&
                    instruction.Argument == constantIndex);
            Assert.Contains(
                program.Instructions,
                instruction => instruction.Opcode is ExprOpcode.OpProfileEnd &&
                    instruction.Argument == constantIndex);
        }
    }

    private static ExprFunction Function(string name, int arity)
    {
        ExprTypeDescriptor[] parameters = [.. Enumerable.Repeat<ExprTypeDescriptor>(ExprTypes.Integer, arity)];
        return new ExprFunction(
            name,
            [new ExprFunctionOverload(parameters, ExprTypes.Integer)],
            static _ => 0L);
    }

    [Fact]
    public void Public_compiler_handles_consumer_constants_and_reports_its_real_depth_limit()
    {
        ExprConfiguration ordinary = ExprConfiguration.Default.WithOptimization(false);
        var nilTree = new SyntaxTree(new ConstantNode(null, default), new SourceText(string.Empty));
        var bytesTree = new SyntaxTree(
            new ConstantNode(new ReadOnlyMemory<byte>("bytes"u8.ToArray()), default),
            new SourceText(string.Empty));

        Assert.Null(ExprEngine.Compile(nilTree, ordinary).Run(
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(
            "bytes"u8.ToArray(),
            Assert.IsType<ReadOnlyMemory<byte>>(ExprEngine.Compile(bytesTree, ordinary).Run(
                cancellationToken: TestContext.Current.CancellationToken)).ToArray());

        SyntaxNode root = new IntegerNode(1, default);
        for (var depth = 0; depth < 1_025; depth++)
        {
            root = new UnaryNode("+", root, default);
        }

        var deepTree = new SyntaxTree(root, new SourceText(string.Empty));
        ExprConfiguration deep = ordinary.WithMaximumCheckDepth(2_000);
        ExprCompilationException exception = Assert.Throws<ExprCompilationException>(() => ExprEngine.Compile(
            deepTree,
            deep,
            new ExprCompilationOptions { EnableProfiling = true }));
        Assert.Contains("compilation depth", exception.Message, StringComparison.Ordinal);
    }

    private static ExprProgram Compile(string source, ExprConfiguration configuration)
    {
        var parserOptions = new SyntaxParserOptions
        {
            MaximumNodeCount = configuration.MaximumNodeCount,
            DisableIfOperator = configuration.DisableIfOperator,
            DisabledBuiltins = configuration.DisabledBuiltins,
            OverriddenBuiltins = new HashSet<string>(configuration.Functions.Keys, StringComparer.Ordinal),
        };
        SyntaxTree tree = new SyntaxParser().Parse(source, parserOptions);
        ExprSemanticModel model = new ExprChecker().Check(tree, configuration);
        return ExprCompiler.Compile(model, configuration);
    }

    private sealed record HostEnvironment(HostValue Host);

    private sealed record HostValue(string Value)
    {
        public string Echo(string prefix) => prefix + Value;
    }

    private sealed record ProviderEnvironment(IntegerProvider Wrapped);

    private sealed record IntegerProvider(long Value) : IExprValueProvider<long>
    {
        public long ToExprValue() => Value;
    }
}
