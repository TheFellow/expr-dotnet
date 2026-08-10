using System;
using System.Collections.Generic;
using Expr.Compilation;
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
    public void Disassembler_recognizes_every_opcode()
    {
        var parser = new SyntaxParser();
        SyntaxTree tree = parser.Parse("nil");
        ExprOpcode[] opcodes = Enum.GetValues<ExprOpcode>();
        var instructions = new List<ExprInstruction>(opcodes.Length);
        foreach (ExprOpcode opcode in opcodes)
        {
            instructions.Add(new ExprInstruction(opcode, 0, tree.Root.Location));
        }

        var program = new ExprProgram(tree, instructions, [42L], [], 0);

        string listing = program.Disassemble();
        foreach (ExprOpcode opcode in opcodes)
        {
            Assert.Contains(opcode.ToString(), listing, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("unknown", listing, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Program_defensively_snapshots_all_structural_collections()
    {
        SyntaxTree tree = new SyntaxParser().Parse("1");
        var instructions = new List<ExprInstruction>
        {
            new(ExprOpcode.OpPush, 0, tree.Root.Location),
        };
        var constants = new List<object?> { 1L };
        var variableNames = new Dictionary<int, string> { [0] = "answer" };
        var program = new ExprProgram(tree, instructions, constants, [], 1, variableNames);

        instructions.Clear();
        constants[0] = 2L;
        variableNames[0] = "changed";

        Assert.Single(program.Instructions);
        Assert.Equal(1L, program.Constants[0]);
        Assert.Equal("answer", program.VariableNames[0]);
    }
}
