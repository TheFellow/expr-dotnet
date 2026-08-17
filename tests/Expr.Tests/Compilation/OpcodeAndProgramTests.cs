using System;
using System.Collections.Generic;
using Expr.Compilation;
using Expr.Execution;
using Expr.Syntax;
using Xunit;

namespace Expr.Tests.Compilation;

public sealed class OpcodeAndProgramTests
{
    // Provenance: inspiration/expr/vm/opcodes.go and vm/program_test.go.
    [Fact]
    public void Opcode_table_preserves_every_upstream_value_and_end_sentinel()
    {
        ExprOpcode[] values = Enum.GetValues<ExprOpcode>();

        Assert.Equal(84, values.Length);
        Assert.Equal(ExprOpcode.OpInvalid, values[0]);
        Assert.Equal(0, (int)ExprOpcode.OpInvalid);
        Assert.Equal(83, (int)ExprOpcode.OpEnd);
        for (var index = 0; index < values.Length; index++)
        {
            Assert.Equal(index, (int)values[index]);
        }
    }

    [Fact]
    public void Compiler_output_disassembles_without_unknown_operands()
    {
        ExprProgram program = ExprEngine.Compile(
            "let items = map(1..3, # * 2); items[1] == 4 ? sum(items) : 0").Program;

        string listing = program.Disassemble();

        Assert.Contains(ExprOpcode.OpStore.ToString(), listing, StringComparison.Ordinal);
        Assert.Contains(ExprOpcode.OpBegin.ToString(), listing, StringComparison.Ordinal);
        Assert.Contains(ExprOpcode.OpJumpIfFalse.ToString(), listing, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", listing, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compiler_output_is_immutable_and_snapshots_consumer_byte_constants()
    {
        byte[] bytes = [1, 2, 3];
        var tree = new SyntaxTree(new BytesNode(bytes, new SourceLocation(0, 1)), new SourceText("0"));
        ExprProgram program = ExprEngine.Compile(tree).Program;

        bytes[0] = 99;

        var stored = Assert.IsType<ReadOnlyMemory<byte>>(Assert.Single(program.Constants));
        Assert.Equal(new byte[] { 1, 2, 3 }, stored.ToArray());
        Assert.Throws<NotSupportedException>(() => ((IList<ExprInstruction>)program.Instructions).Clear());
    }

    [Fact]
    public void Public_program_constructor_rejects_invalid_bytecode_immediately()
    {
        var tree = new SyntaxTree(new NilNode(default), new SourceText(string.Empty));

        _ = Assert.Throws<ExprExecutionException>(() =>
            new ExprProgram(tree, [new ExprInstruction(ExprOpcode.OpInvalid, 0, default)], [], [], 0));
        _ = Assert.Throws<ExprExecutionException>(() =>
            new ExprProgram(tree, [], [], [], 0));
    }
}
