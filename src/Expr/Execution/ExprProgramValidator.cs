using System;
using System.Text;
using Expr.Compilation;
using Expr.Syntax;

namespace Expr.Execution;

internal static class ExprProgramValidator
{
    private const int MaximumCallArguments = 65_536;

    internal static void Validate(ExprProgram program, ExprEvaluationOptions options)
    {
        ValidateOptions(options);
        if (program.Instructions.Count is 0)
        {
            Fail(program, -1, default, "program contains no instructions");
        }

        if (program.VariableCount > options.MaximumStackDepth)
        {
            Fail(program, -1, default, "program local-variable count exceeds the configured limit");
        }

        int sourceLength = 0;
        foreach (Rune _ in program.Source.Text.EnumerateRunes())
        {
            sourceLength++;
        }

        for (var index = 0; index < program.Instructions.Count; index++)
        {
            ExprInstruction instruction = program.Instructions[index];
            SourceLocation location = instruction.Location;
            if (location.Start < 0 || location.End < location.Start || location.End > sourceLength)
            {
                Fail(program, index, default, "instruction contains an invalid source location");
            }

            ValidateInstruction(program, index, instruction);
        }
    }

    private static void ValidateOptions(ExprEvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.WorkBudget is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "WorkBudget must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumStackDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumScopeDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumCollectionLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumRegularExpressionLength);
        if (options.RegularExpressionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RegularExpressionTimeout must be positive.");
        }
    }

    private static void ValidateInstruction(ExprProgram program, int index, ExprInstruction instruction)
    {
        int argument = instruction.Argument;
        switch (instruction.Opcode)
        {
            case ExprOpcode.OpInvalid:
                Fail(program, index, instruction.Location, "invalid opcode");
                return;
            case ExprOpcode.OpPush:
                RequireConstant(program, index, instruction, null);
                return;
            case ExprOpcode.OpInt:
                return;
            case ExprOpcode.OpPop:
                RequireZero(program, index, instruction);
                return;
            case ExprOpcode.OpStore:
            case ExprOpcode.OpLoadVar:
                if ((uint)argument >= (uint)program.VariableCount)
                {
                    Fail(program, index, instruction.Location, "local-variable operand is out of range");
                }

                return;
            case ExprOpcode.OpLoadConst:
                RequireConstant(program, index, instruction, typeof(string));
                return;
            case ExprOpcode.OpLoadField:
            case ExprOpcode.OpLoadMethod:
            case ExprOpcode.OpFetchField:
            case ExprOpcode.OpMethod:
                RequireConstant(program, index, instruction, typeof(ExprMemberOperand));
                return;
            case ExprOpcode.OpLoadFast:
                RequireConstant(program, index, instruction, typeof(string));
                return;
            case ExprOpcode.OpLoadFunc:
            case ExprOpcode.OpCall0:
            case ExprOpcode.OpCall1:
            case ExprOpcode.OpCall2:
            case ExprOpcode.OpCall3:
            case ExprOpcode.OpCallBuiltin1:
                RequireFunction(program, index, instruction);
                return;
            case ExprOpcode.OpLoadEnv:
            case ExprOpcode.OpFetch:
            case ExprOpcode.OpTrue:
            case ExprOpcode.OpFalse:
            case ExprOpcode.OpNil:
            case ExprOpcode.OpNegate:
            case ExprOpcode.OpNot:
            case ExprOpcode.OpEqual:
            case ExprOpcode.OpEqualInt:
            case ExprOpcode.OpEqualString:
            case ExprOpcode.OpIn:
            case ExprOpcode.OpLess:
            case ExprOpcode.OpMore:
            case ExprOpcode.OpLessOrEqual:
            case ExprOpcode.OpMoreOrEqual:
            case ExprOpcode.OpAdd:
            case ExprOpcode.OpSubtract:
            case ExprOpcode.OpMultiply:
            case ExprOpcode.OpDivide:
            case ExprOpcode.OpModulo:
            case ExprOpcode.OpExponent:
            case ExprOpcode.OpRange:
            case ExprOpcode.OpMatches:
            case ExprOpcode.OpContains:
            case ExprOpcode.OpStartsWith:
            case ExprOpcode.OpEndsWith:
            case ExprOpcode.OpSlice:
            case ExprOpcode.OpArray:
            case ExprOpcode.OpMap:
            case ExprOpcode.OpLen:
            case ExprOpcode.OpDeref:
            case ExprOpcode.OpIncrementIndex:
            case ExprOpcode.OpDecrementIndex:
            case ExprOpcode.OpIncrementCount:
            case ExprOpcode.OpGetIndex:
            case ExprOpcode.OpGetCount:
            case ExprOpcode.OpGetLen:
            case ExprOpcode.OpGetAcc:
            case ExprOpcode.OpSetAcc:
            case ExprOpcode.OpSetIndex:
            case ExprOpcode.OpPointer:
            case ExprOpcode.OpThrow:
            case ExprOpcode.OpGroupBy:
            case ExprOpcode.OpSortBy:
            case ExprOpcode.OpSort:
            case ExprOpcode.OpBegin:
            case ExprOpcode.OpAnd:
            case ExprOpcode.OpOr:
            case ExprOpcode.OpEnd:
                RequireZero(program, index, instruction);
                return;
            case ExprOpcode.OpJump:
            case ExprOpcode.OpJumpIfTrue:
            case ExprOpcode.OpJumpIfFalse:
            case ExprOpcode.OpJumpIfNil:
            case ExprOpcode.OpJumpIfNotNil:
            case ExprOpcode.OpJumpIfEnd:
                if (argument < 0 || (long)index + 1 + argument > program.Instructions.Count)
                {
                    Fail(program, index, instruction.Location, "forward jump target is out of range");
                }

                return;
            case ExprOpcode.OpJumpBackward:
                if (argument <= 0 || (long)index + 1 - argument < 0)
                {
                    Fail(program, index, instruction.Location, "backward jump target is out of range");
                }

                return;
            case ExprOpcode.OpMatchesConst:
                RequireConstant(program, index, instruction, typeof(ExprRegularExpressionOperand));
                return;
            case ExprOpcode.OpCall:
            case ExprOpcode.OpCallN:
            case ExprOpcode.OpCallFast:
            case ExprOpcode.OpCallSafe:
            case ExprOpcode.OpCallTyped:
                if (argument < 0 || argument > MaximumCallArguments)
                {
                    Fail(program, index, instruction.Location, "call argument count is out of range");
                }

                return;
            case ExprOpcode.OpCast:
                if (!Enum.IsDefined((ExprCastKind)argument))
                {
                    Fail(program, index, instruction.Location, "cast operand is invalid");
                }

                return;
            case ExprOpcode.OpCreate:
                if (argument is not (1 or 2))
                {
                    Fail(program, index, instruction.Location, "create operand is invalid");
                }

                return;
            case ExprOpcode.OpProfileStart:
            case ExprOpcode.OpProfileEnd:
                RequireConstant(program, index, instruction, typeof(ExprProfilePoint));
                return;
            default:
                Fail(program, index, instruction.Location, "unknown opcode");
                return;
        }
    }

    private static void RequireZero(ExprProgram program, int index, ExprInstruction instruction)
    {
        if (instruction.Argument is not 0)
        {
            Fail(program, index, instruction.Location, "instruction has an unexpected operand");
        }
    }

    private static void RequireConstant(
        ExprProgram program,
        int index,
        ExprInstruction instruction,
        Type? expectedType)
    {
        if ((uint)instruction.Argument >= (uint)program.Constants.Count)
        {
            Fail(program, index, instruction.Location, "constant operand is out of range");
        }

        object? value = program.Constants[instruction.Argument];
        if (expectedType is not null && !expectedType.IsInstanceOfType(value))
        {
            Fail(program, index, instruction.Location, "constant operand has an invalid type");
        }
    }

    private static void RequireFunction(ExprProgram program, int index, ExprInstruction instruction)
    {
        if ((uint)instruction.Argument >= (uint)program.Functions.Count)
        {
            Fail(program, index, instruction.Location, "function operand is out of range");
        }
    }

    private static void Fail(
        ExprProgram program,
        int instructionIndex,
        SourceLocation location,
        string message) =>
        throw new ExprExecutionException(message, instructionIndex, location, program.Source);
}
