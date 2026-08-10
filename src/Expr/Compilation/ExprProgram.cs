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
    private readonly IReadOnlyList<ExprInstruction> instructions;
    private readonly IReadOnlyList<ExprOpcode> bytecode;
    private readonly IReadOnlyList<int> arguments;
    private readonly IReadOnlyList<SourceLocation> locations;
    private readonly IReadOnlyList<object?> constants;
    private readonly IReadOnlyList<ExprFunction> functions;
    private readonly IReadOnlyList<ExprProfilePoint> profilePoints;
    private readonly IReadOnlyDictionary<int, string> variableNames;
    private readonly IReadOnlyDictionary<int, string> functionNames;

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

        ExprInstruction[] instructionArray = instructions.ToArray();
        this.instructions = Array.AsReadOnly(instructionArray);
        bytecode = Array.AsReadOnly(instructionArray.Select(static instruction => instruction.Opcode).ToArray());
        arguments = Array.AsReadOnly(instructionArray.Select(static instruction => instruction.Argument).ToArray());
        locations = Array.AsReadOnly(instructionArray.Select(static instruction => instruction.Location).ToArray());
        this.constants = Array.AsReadOnly(constants.Select(SnapshotConstant).ToArray());
        this.functions = Array.AsReadOnly(functions.ToArray());
        this.profilePoints = Array.AsReadOnly((profilePoints ?? []).ToArray());
        VariableCount = variableCount;
        this.variableNames = new ReadOnlyDictionary<int, string>(
            variableNames is null ? [] : new Dictionary<int, string>(variableNames));
        this.functionNames = new ReadOnlyDictionary<int, string>(
            functionNames is null ? [] : new Dictionary<int, string>(functionNames));
    }

    /// <summary>Gets the checked syntax tree from which this program was compiled.</summary>
    public SyntaxTree SyntaxTree { get; }

    /// <summary>Gets the original source text.</summary>
    public SourceText Source => SyntaxTree.Source;

    /// <summary>Gets the original checked root node.</summary>
    public SyntaxNode Root => SyntaxTree.Root;

    /// <summary>Gets instructions with arguments and source locations.</summary>
    public IReadOnlyList<ExprInstruction> Instructions => instructions;

    /// <summary>Gets opcodes in instruction order.</summary>
    public IReadOnlyList<ExprOpcode> Bytecode => bytecode;

    /// <summary>Gets opcode arguments in instruction order.</summary>
    public IReadOnlyList<int> Arguments => arguments;

    /// <summary>Gets source ranges in instruction order.</summary>
    public IReadOnlyList<SourceLocation> Locations => locations;

    /// <summary>Gets the immutable constant table.</summary>
    public IReadOnlyList<object?> Constants => constants;

    /// <summary>Gets known functions referenced by bytecode.</summary>
    public IReadOnlyList<ExprFunction> Functions => functions;

    /// <summary>Gets profiling points referenced by profile boundary instructions.</summary>
    public IReadOnlyList<ExprProfilePoint> ProfilePoints => profilePoints;

    /// <summary>Gets the number of local-variable slots required by the program.</summary>
    public int VariableCount { get; }

    /// <summary>Gets local-variable debug names by slot.</summary>
    public IReadOnlyDictionary<int, string> VariableNames => variableNames;

    /// <summary>Gets known-function debug names by table index.</summary>
    public IReadOnlyDictionary<int, string> FunctionNames => functionNames;

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
        for (var index = 0; index < instructions.Count; index++)
        {
            ExprInstruction instruction = instructions[index];
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
                WriteIndexed(writer, argument, variableNames.GetValueOrDefault(argument));
                return;
            case ExprOpcode.OpLoadFunc:
            case ExprOpcode.OpCall0:
            case ExprOpcode.OpCall1:
            case ExprOpcode.OpCall2:
            case ExprOpcode.OpCall3:
            case ExprOpcode.OpCallBuiltin1:
                WriteIndexed(writer, argument, functionNames.GetValueOrDefault(argument));
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
        if ((uint)index >= (uint)constants.Count)
        {
            return "out of range";
        }

        object? value = constants[index];
        return value switch
        {
            null => "nil",
            ReadOnlyMemory<byte> bytes => Convert.ToHexString(bytes.Span),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static object? SnapshotConstant(object? value) => value switch
    {
        byte[] bytes => new ReadOnlyMemory<byte>(bytes.ToArray()),
        ReadOnlyMemory<byte> bytes => new ReadOnlyMemory<byte>(bytes.ToArray()),
        _ => value,
    };
}
