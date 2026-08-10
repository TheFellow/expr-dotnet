using System;
using System.Collections.Generic;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Compilation;

public sealed class PredicateCompilerTests
{
    public static TheoryData<string, ExprOpcode[]> PredicateMarkers => new()
    {
        { "all([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpJumpIfFalse, ExprOpcode.OpEnd] },
        { "none([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpNot, ExprOpcode.OpEnd] },
        { "any([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpJumpIfTrue, ExprOpcode.OpEnd] },
        { "one([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpIncrementCount, ExprOpcode.OpEnd] },
        { "filter([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpIncrementCount, ExprOpcode.OpArray] },
        { "map([1, 2], {# * 2})", [ExprOpcode.OpBegin, ExprOpcode.OpGetLen, ExprOpcode.OpArray] },
        { "count([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpGetCount, ExprOpcode.OpEnd] },
        { "sum([1, 2], {#})", [ExprOpcode.OpBegin, ExprOpcode.OpGetAcc, ExprOpcode.OpEnd] },
        { "find([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpPointer, ExprOpcode.OpEnd] },
        { "findIndex([1, 2], {# > 0})", [ExprOpcode.OpBegin, ExprOpcode.OpGetIndex, ExprOpcode.OpEnd] },
        { "findLast([1, 2], {# > 0})", [ExprOpcode.OpSetIndex, ExprOpcode.OpDecrementIndex, ExprOpcode.OpEnd] },
        { "findLastIndex([1, 2], {# > 0})", [ExprOpcode.OpSetIndex, ExprOpcode.OpGetIndex, ExprOpcode.OpEnd] },
        { "groupBy([1, 2], {#})", [ExprOpcode.OpCreate, ExprOpcode.OpGroupBy, ExprOpcode.OpEnd] },
        { "sortBy([1, 2], {#}, 'desc')", [ExprOpcode.OpCreate, ExprOpcode.OpSortBy, ExprOpcode.OpSort] },
        { "reduce([1, 2], {#acc + #})", [ExprOpcode.OpJumpIfEnd, ExprOpcode.OpSetAcc, ExprOpcode.OpEnd] },
    };

    // Provenance: inspiration/expr/compiler/compiler.go BuiltinNode and compiler_test.go.
    [Theory]
    [MemberData(nameof(PredicateMarkers))]
    public void Every_upstream_predicate_family_lowers_to_its_dedicated_loop(
        string source,
        ExprOpcode[] markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        ExprProgram program = Compile(source);

        foreach (ExprOpcode marker in markers)
        {
            Assert.Contains(marker, program.Bytecode);
        }

        Assert.DoesNotContain(program.Arguments, static argument => argument == 12_345);
        AssertValidJumpTargets(program);
    }

    [Fact]
    public void Optional_chain_and_coalescing_share_the_chain_exit()
    {
        ExprConfiguration configuration = ExprConfiguration.Default.AllowUndefinedVariables().WithOptimization(false);
        ExprProgram program = Compile("foo?.bar ?? 42", configuration);

        Assert.Equal(ExprOpcode.OpLoadConst, program.Bytecode[0]);
        Assert.Contains(ExprOpcode.OpJumpIfNil, program.Bytecode);
        Assert.Contains(ExprOpcode.OpJumpIfNotNil, program.Bytecode);
        Assert.Equal(ExprOpcode.OpPush, program.Bytecode[^1]);
        Assert.DoesNotContain(program.Arguments, static argument => argument == 12_345);
    }

    private static ExprProgram Compile(string source, ExprConfiguration? configuration = null)
    {
        ExprConfiguration effective = configuration ?? ExprConfiguration.Default.WithOptimization(false);
        var parserOptions = new SyntaxParserOptions
        {
            MaximumNodeCount = effective.MaximumNodeCount,
            DisabledBuiltins = effective.DisabledBuiltins,
            OverriddenBuiltins = new HashSet<string>(effective.Functions.Keys, StringComparer.Ordinal),
        };
        SyntaxTree tree = new SyntaxParser().Parse(source, parserOptions);
        ExprSemanticModel model = new ExprChecker().Check(tree, effective);
        return ExprCompiler.Compile(model, effective);
    }

    private static void AssertValidJumpTargets(ExprProgram program)
    {
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            ExprInstruction instruction = program.Instructions[index];
            if (instruction.Opcode is ExprOpcode.OpJump or ExprOpcode.OpJumpIfTrue or
                ExprOpcode.OpJumpIfFalse or ExprOpcode.OpJumpIfNil or ExprOpcode.OpJumpIfNotNil or
                ExprOpcode.OpJumpIfEnd)
            {
                int target = index + 1 + instruction.Argument;
                Assert.InRange(target, index + 1, program.Instructions.Count);
            }
            else if (instruction.Opcode is ExprOpcode.OpJumpBackward)
            {
                int target = index + 1 - instruction.Argument;
                Assert.InRange(target, 0, index);
            }
        }
    }
}
