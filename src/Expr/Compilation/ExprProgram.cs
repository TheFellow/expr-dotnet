using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Expr.Runtime;
using Expr.Syntax;

namespace Expr.Compilation;

/// <summary>Contains immutable bytecode produced from a checked Expr syntax tree.</summary>
public sealed class ExprProgram
{

    /// <summary>Initializes a program from an instruction stream and its immutable metadata.</summary>
    /// <param name="syntaxTree">The checked source syntax.</param>
    /// <param name="instructions">The ordered instructions.</param>
    /// <param name="constants">The constant table.</param>
    /// <param name="functions">The known-function table.</param>
    /// <param name="variableCount">The required local-variable slot count.</param>
    /// <param name="variableNames">Optional variable debug names by slot.</param>
    /// <param name="functionNames">Optional function debug names by table index.</param>
    /// <param name="profilePoints">Optional profiling metadata in identifier order.</param>
    public ExprProgram(
        SyntaxTree syntaxTree,
        IEnumerable<ExprInstruction> instructions,
        IEnumerable<object?> constants,
        IEnumerable<ExprFunction> functions,
        int variableCount,
        IReadOnlyDictionary<int, string>? variableNames = null,
        IReadOnlyDictionary<int, string>? functionNames = null,
        IEnumerable<ExprProfilePoint>? profilePoints = null)
    {
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(constants);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(variableCount);

        ExprInstruction[] instructionArray = [.. instructions];
        Instructions = Array.AsReadOnly(instructionArray);
        Bytecode = Array.AsReadOnly(instructionArray.Select(static instruction => instruction.Opcode).ToArray());
        Arguments = Array.AsReadOnly(instructionArray.Select(static instruction => instruction.Argument).ToArray());
        Locations = Array.AsReadOnly(instructionArray.Select(static instruction => instruction.Location).ToArray());
        Constants = Array.AsReadOnly(constants.Select(SnapshotConstant).ToArray());
        Functions = Array.AsReadOnly(functions.ToArray());
        ProfilePoints = Array.AsReadOnly((profilePoints ?? []).ToArray());
        VariableCount = variableCount;
        VariableNames = new ReadOnlyDictionary<int, string>(
            variableNames is null ? [] : new Dictionary<int, string>(variableNames));
        FunctionNames = new ReadOnlyDictionary<int, string>(
            functionNames is null ? [] : new Dictionary<int, string>(functionNames));
    }

    /// <summary>Gets the checked syntax tree from which this program was compiled.</summary>
    public SyntaxTree SyntaxTree { get; }

    /// <summary>Gets the original source text.</summary>
    public SourceText Source => SyntaxTree.Source;

    /// <summary>Gets the original checked root node.</summary>
    public SyntaxNode Root => SyntaxTree.Root;

    /// <summary>Gets instructions with arguments and source locations.</summary>
    public IReadOnlyList<ExprInstruction> Instructions { get; }

    /// <summary>Gets opcodes in instruction order.</summary>
    public IReadOnlyList<ExprOpcode> Bytecode { get; }

    /// <summary>Gets opcode arguments in instruction order.</summary>
    public IReadOnlyList<int> Arguments { get; }

    /// <summary>Gets source ranges in instruction order.</summary>
    public IReadOnlyList<SourceLocation> Locations { get; }

    /// <summary>Gets the immutable constant table.</summary>
    public IReadOnlyList<object?> Constants { get; }

    /// <summary>Gets known functions referenced by bytecode.</summary>
    public IReadOnlyList<ExprFunction> Functions { get; }

    /// <summary>Gets profiling points referenced by profile boundary instructions.</summary>
    public IReadOnlyList<ExprProfilePoint> ProfilePoints { get; }

    /// <summary>Gets the number of local-variable slots required by the program.</summary>
    public int VariableCount { get; }

    /// <summary>Gets local-variable debug names by slot.</summary>
    public IReadOnlyDictionary<int, string> VariableNames { get; }

    /// <summary>Gets known-function debug names by table index.</summary>
    public IReadOnlyDictionary<int, string> FunctionNames { get; }

    /// <summary>Returns a stable, human-readable bytecode listing.</summary>
    /// <returns>The disassembled program.</returns>
    public string Disassemble()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Disassemble(writer);
        return writer.ToString();
    }

    /// <summary>Writes a stable, human-readable bytecode listing.</summary>
    /// <param name="writer">The destination writer.</param>
    public void Disassemble(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var index = 0; index < Instructions.Count; index++)
        {
            ExprInstruction instruction = Instructions[index];
            writer.Write(index.ToString(CultureInfo.InvariantCulture));
            writer.Write("\t");
            writer.Write(instruction.Opcode);
            WriteOperand(writer, index, instruction);
            writer.WriteLine();
        }
    }

    private void WriteOperand(TextWriter writer, int index, ExprInstruction instruction)
    {
        int argument = instruction.Argument;
        switch (instruction.Opcode)
        {
            case ExprOpcode.OpPush:
            case ExprOpcode.OpLoadConst:
            case ExprOpcode.OpLoadField:
            case ExprOpcode.OpLoadFast:
            case ExprOpcode.OpLoadMethod:
            case ExprOpcode.OpFetchField:
            case ExprOpcode.OpMethod:
            case ExprOpcode.OpMatchesConst:
            case ExprOpcode.OpProfileStart:
            case ExprOpcode.OpProfileEnd:
                WriteIndexed(writer, argument, ConstantDisplay(argument));
                return;
            case ExprOpcode.OpStore:
            case ExprOpcode.OpLoadVar:
                WriteIndexed(writer, argument, VariableNames.GetValueOrDefault(argument));
                return;
            case ExprOpcode.OpLoadFunc:
            case ExprOpcode.OpCall0:
            case ExprOpcode.OpCall1:
            case ExprOpcode.OpCall2:
            case ExprOpcode.OpCall3:
            case ExprOpcode.OpCallBuiltin1:
                WriteIndexed(writer, argument, FunctionNames.GetValueOrDefault(argument));
                return;
            case ExprOpcode.OpJump:
            case ExprOpcode.OpJumpIfTrue:
            case ExprOpcode.OpJumpIfFalse:
            case ExprOpcode.OpJumpIfNil:
            case ExprOpcode.OpJumpIfNotNil:
            case ExprOpcode.OpJumpIfEnd:
                WriteIndexed(writer, argument, $"({index + 1 + argument})");
                return;
            case ExprOpcode.OpJumpBackward:
                WriteIndexed(writer, argument, $"({index + 1 - argument})");
                return;
            case ExprOpcode.OpInt:
            case ExprOpcode.OpCall:
            case ExprOpcode.OpCallN:
            case ExprOpcode.OpCallFast:
            case ExprOpcode.OpCallSafe:
            case ExprOpcode.OpCallTyped:
            case ExprOpcode.OpCast:
            case ExprOpcode.OpCreate:
                WriteIndexed(writer, argument, null);
                return;
            default:
                return;
        }
    }

    private static void WriteIndexed(TextWriter writer, int argument, string? detail)
    {
        writer.Write("\t<");
        writer.Write(argument.ToString(CultureInfo.InvariantCulture));
        writer.Write('>');
        if (!string.IsNullOrEmpty(detail))
        {
            writer.Write("\t");
            writer.Write(detail);
        }
    }

    private string ConstantDisplay(int index)
    {
        if ((uint)index >= (uint)Constants.Count)
        {
            return "out of range";
        }

        return ExprDisplay.Value(Constants[index]);
    }

    private static object? SnapshotConstant(object? value) => value switch
    {
        byte[] bytes => new ReadOnlyMemory<byte>([.. bytes]),
        ReadOnlyMemory<byte> bytes => new ReadOnlyMemory<byte>(bytes.ToArray()),
        _ => value,
    };
}
