using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Checking;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Compilation;

/// <summary>Lowers checked Expr syntax into immutable virtual-machine bytecode.</summary>
public static class ExprCompiler
{
    /// <summary>Compiles a checked syntax tree.</summary>
    /// <param name="semanticModel">The checked and semantically patched tree.</param>
    /// <param name="configuration">The configuration used to check the tree.</param>
    /// <param name="options">Optional bytecode-generation settings.</param>
    /// <returns>An immutable program ready for VM execution.</returns>
    public static ExprProgram Compile(
        ExprSemanticModel semanticModel,
        ExprConfiguration? configuration = null,
        ExprCompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ExprConfiguration effectiveConfiguration = configuration ?? ExprConfiguration.Default;
        return new Compiler(
            semanticModel,
            effectiveConfiguration,
            options ?? ExprCompilationOptions.Default).Compile();
    }

    private sealed class Compiler
    {
        private const int JumpPlaceholder = 12_345;
        private const int MaximumInstructionCount = 1_000_000;
        private const int MaximumCompilationDepth = 1_024;

        private readonly ExprSemanticModel semanticModel;
        private readonly ExprConfiguration configuration;
        private readonly ExprCompilationOptions options;
        private readonly List<MutableInstruction> instructions = [];
        private readonly List<object?> constants = [];
        private readonly Dictionary<object, int> constantIndices = [];
        private readonly List<ExprFunction> functions = [];
        private readonly Dictionary<ExprFunction, int> functionIndices = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, string> functionNames = [];
        private readonly Dictionary<int, string> variableNames = [];
        private readonly List<VariableScope> variables = [];
        private readonly List<SyntaxNode> nodes = [];
        private readonly Stack<List<int>> optionalChains = [];
        private readonly List<ExprProfilePoint> profilePoints = [];
        private readonly Stack<ExprProfilePoint> profileStack = [];
        private int variableCount;
        private int depth;

        internal Compiler(
            ExprSemanticModel semanticModel,
            ExprConfiguration configuration,
            ExprCompilationOptions options)
        {
            this.semanticModel = semanticModel;
            this.configuration = configuration;
            this.options = options;
        }

        internal ExprProgram Compile()
        {
            CompileValue(semanticModel.SyntaxTree.Root);
            EmitResultCast(semanticModel.SyntaxTree.Root.Location);
            if (configuration.Optimize)
            {
                OptimizeJumps();
            }

            return new ExprProgram(
                semanticModel.SyntaxTree,
                instructions.Select(static instruction => instruction.ToImmutable()),
                constants,
                functions,
                variableCount,
                variableNames,
                functionNames,
                profilePoints);
        }

        private void CompileNode(SyntaxNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            depth++;
            int maximumDepth = Math.Min(configuration.MaximumCheckDepth, MaximumCompilationDepth);
            if (depth > maximumDepth)
            {
                depth--;
                throw new ExprCompilationException(
                    $"compilation depth exceeds maximum of {maximumDepth}",
                    node.Location);
            }

            try
            {
                nodes.Add(node);
                ExprProfilePoint? profilePoint = BeginProfile(node);
                switch (node)
                {
                    case NilNode:
                        Emit(node, ExprOpcode.OpNil);
                        break;
                    case IdentifierNode identifier:
                        CompileIdentifier(identifier);
                        break;
                    case IntegerNode integer:
                        EmitPush(integer, integer.Value);
                        break;
                    case FloatNode number:
                        EmitPush(number, number.Value);
                        break;
                    case BooleanNode boolean:
                        Emit(boolean, boolean.Value ? ExprOpcode.OpTrue : ExprOpcode.OpFalse);
                        break;
                    case StringNode text:
                        EmitPush(text, text.Value);
                        break;
                    case BytesNode bytes:
                        EmitPush(bytes, bytes.Value.ToArray());
                        break;
                    case ConstantNode constant:
                        CompileConstant(constant);
                        break;
                    case UnaryNode unary:
                        CompileUnary(unary);
                        break;
                    case BinaryNode binary:
                        CompileBinary(binary);
                        break;
                    case ChainNode chain:
                        CompileChain(chain);
                        break;
                    case MemberNode member:
                        CompileMember(member);
                        break;
                    case SliceNode slice:
                        CompileSlice(slice);
                        break;
                    case CallNode call:
                        CompileCall(call);
                        break;
                    case BuiltinNode builtin:
                        CompileBuiltin(builtin);
                        break;
                    case PredicateNode predicate:
                        CompileValue(predicate.Body);
                        break;
                    case PointerNode pointer:
                        CompilePointer(pointer);
                        break;
                    case VariableDeclaratorNode variable:
                        CompileVariable(variable);
                        break;
                    case SequenceNode sequence:
                        CompileSequence(sequence);
                        break;
                    case ConditionalNode conditional:
                        CompileConditional(conditional);
                        break;
                    case ArrayNode array:
                        CompileArray(array);
                        break;
                    case MapNode map:
                        CompileMap(map);
                        break;
                    case PairNode pair:
                        CompileValue(pair.Key);
                        CompileValue(pair.Value);
                        break;
                    default:
                        throw new ExprCompilationException(
                            $"unsupported syntax node {node.GetType().Name}",
                            node.Location);
                }

                EndProfile(node, profilePoint);
            }
            finally
            {
                if (options.EnableProfiling && profileStack.Count > 0 &&
                    ReferenceEquals(profileStack.Peek(), profilePoints[^1]))
                {
                    profileStack.Pop();
                }

                nodes.RemoveAt(nodes.Count - 1);
                depth--;
            }
        }

        private void CompileIdentifier(IdentifierNode node)
        {
            if (TryFindVariable(node.Name, out int slot))
            {
                Emit(node, ExprOpcode.OpLoadVar, slot);
                return;
            }

            if (string.Equals(node.Name, "$env", StringComparison.Ordinal))
            {
                Emit(node, ExprOpcode.OpLoadEnv);
                return;
            }

            ExprNodeSemantics? semantics = Semantics(node);
            if (semantics?.Function is ExprFunction function)
            {
                Emit(node, ExprOpcode.OpLoadFunc, AddFunction(function));
                return;
            }

            if (semantics?.Member is ExprMemberBinding binding)
            {
                if (binding.Kind is ExprMemberBindingKind.Environment && IsFastEnvironment())
                {
                    Emit(node, ExprOpcode.OpLoadFast, AddConstant(node.Name));
                    return;
                }

                ExprMemberOperand operand = MemberOperand(binding);
                ExprOpcode opcode = binding.Kind is ExprMemberBindingKind.ClrMethod
                    ? ExprOpcode.OpLoadMethod
                    : ExprOpcode.OpLoadField;
                Emit(node, opcode, AddConstant(operand));
                return;
            }

            Emit(node, ExprOpcode.OpLoadConst, AddConstant(node.Name));
        }

        private void CompileConstant(ConstantNode node)
        {
            if (node.Value is null)
            {
                Emit(node, ExprOpcode.OpNil);
            }
            else
            {
                EmitPush(node, node.Value);
            }
        }

        private void CompileUnary(UnaryNode node)
        {
            CompileValue(node.Operand);
            switch (node.Operator)
            {
                case "!":
                case "not":
                    Emit(node, ExprOpcode.OpNot);
                    break;
                case "+":
                    break;
                case "-":
                    Emit(node, ExprOpcode.OpNegate);
                    break;
                default:
                    throw UnknownOperator(node.Operator, node.Location);
            }
        }

        private void CompileBinary(BinaryNode node)
        {
            switch (node.Operator)
            {
                case "==":
                    CompileEquality(node);
                    return;
                case "!=":
                    CompileEquality(node);
                    Emit(node, ExprOpcode.OpNot);
                    return;
                case "or":
                case "||":
                    CompileLogical(node, when: true, ExprOpcode.OpOr);
                    return;
                case "and":
                case "&&":
                    CompileLogical(node, when: false, ExprOpcode.OpAnd);
                    return;
                case "??":
                    CompileValue(node.Left);
                    int notNil = Emit(node, ExprOpcode.OpJumpIfNotNil, JumpPlaceholder);
                    Emit(node, ExprOpcode.OpPop);
                    CompileValue(node.Right);
                    PatchJump(notNil);
                    return;
                case "matches" when node.Right is StringNode pattern:
                    CompileConstantMatch(node, pattern);
                    return;
            }

            CompileValue(node.Left);
            CompileValue(node.Right);
            Emit(node, node.Operator switch
            {
                "<" => ExprOpcode.OpLess,
                ">" => ExprOpcode.OpMore,
                "<=" => ExprOpcode.OpLessOrEqual,
                ">=" => ExprOpcode.OpMoreOrEqual,
                "+" => ExprOpcode.OpAdd,
                "-" => ExprOpcode.OpSubtract,
                "*" => ExprOpcode.OpMultiply,
                "/" => ExprOpcode.OpDivide,
                "%" => ExprOpcode.OpModulo,
                "**" or "^" => ExprOpcode.OpExponent,
                "in" => ExprOpcode.OpIn,
                "matches" => ExprOpcode.OpMatches,
                "contains" => ExprOpcode.OpContains,
                "startsWith" => ExprOpcode.OpStartsWith,
                "endsWith" => ExprOpcode.OpEndsWith,
                ".." => ExprOpcode.OpRange,
                _ => throw UnknownOperator(node.Operator, node.Location),
            });
        }

        private void CompileEquality(BinaryNode node)
        {
            CompileValue(node.Left);
            CompileValue(node.Right);
            ExprTypeDescriptor left = Semantics(node.Left)?.Type ?? ExprTypes.Any;
            ExprTypeDescriptor right = Semantics(node.Right)?.Type ?? ExprTypes.Any;
            ExprOpcode opcode = left.Kind == right.Kind
                ? left.Kind switch
                {
                    ExprTypeKind.Integer => ExprOpcode.OpEqualInt,
                    ExprTypeKind.String => ExprOpcode.OpEqualString,
                    _ => ExprOpcode.OpEqual,
                }
                : ExprOpcode.OpEqual;
            Emit(node, opcode);
        }

        private void CompileLogical(BinaryNode node, bool when, ExprOpcode nonShortCircuitOpcode)
        {
            CompileValue(node.Left);
            if (!configuration.ShortCircuit)
            {
                CompileValue(node.Right);
                Emit(node, nonShortCircuitOpcode);
                return;
            }

            int end = Emit(
                node,
                when ? ExprOpcode.OpJumpIfTrue : ExprOpcode.OpJumpIfFalse,
                JumpPlaceholder);
            Emit(node, ExprOpcode.OpPop);
            CompileValue(node.Right);
            PatchJump(end);
        }

        private void CompileConstantMatch(BinaryNode node, StringNode pattern)
        {
            if (pattern.Value.Length > configuration.MaximumRegularExpressionLength)
            {
                throw new ExprCompilationException(
                    $"regular expression exceeds configured maximum length of {configuration.MaximumRegularExpressionLength}",
                    pattern.Location);
            }

            ExprRegularExpressionOperand expression;
            try
            {
                expression = new ExprRegularExpressionOperand(
                    pattern.Value,
                    configuration.RegularExpressionTimeout);
            }
            catch (ArgumentException exception)
            {
                throw new ExprCompilationException(exception.Message, pattern.Location, exception);
            }
            catch (NotSupportedException exception)
            {
                throw new ExprCompilationException(exception.Message, pattern.Location, exception);
            }

            CompileValue(node.Left);
            Emit(node, ExprOpcode.OpMatchesConst, AddConstant(expression));
        }

        private void CompileChain(ChainNode node)
        {
            var jumps = new List<int>();
            optionalChains.Push(jumps);
            CompileValue(node.Expression);
            optionalChains.Pop();
            foreach (int jump in jumps)
            {
                PatchJump(jump);
            }

            if (!ParentIsNilCoalescing())
            {
                int nonNil = Emit(node, ExprOpcode.OpJumpIfNotNil, JumpPlaceholder);
                Emit(node, ExprOpcode.OpPop);
                Emit(node, ExprOpcode.OpNil);
                PatchJump(nonNil);
            }
        }

        private void CompileMember(MemberNode node)
        {
            ExprNodeSemantics? semantics = Semantics(node);
            ExprMemberBinding? binding = semantics?.Member;
            if (node.Target is IdentifierNode { Name: "$env" } && binding is not null)
            {
                Emit(node, ExprOpcode.OpLoadField, AddConstant(MemberOperand(binding)));
                return;
            }

            CompileValue(node.Target);
            EmitOptionalJump(node);

            if (binding is { Kind: ExprMemberBindingKind.ClrMethod })
            {
                Emit(node, ExprOpcode.OpMethod, AddConstant(MemberOperand(binding)));
                return;
            }

            if (binding is { Kind: ExprMemberBindingKind.ClrMember })
            {
                Emit(node, ExprOpcode.OpFetchField, AddConstant(MemberOperand(binding)));
                return;
            }

            CompileValue(node.Property);
            Emit(node, ExprOpcode.OpFetch);
        }

        private void EmitOptionalJump(MemberNode node)
        {
            if (!node.Optional || optionalChains.Count is 0)
            {
                return;
            }

            int jump = Emit(node, ExprOpcode.OpJumpIfNil, JumpPlaceholder);
            optionalChains.Peek().Add(jump);
        }

        private void CompileSlice(SliceNode node)
        {
            CompileValue(node.Target);
            if (node.To is null)
            {
                Emit(node, ExprOpcode.OpLen);
            }
            else
            {
                CompileValue(node.To);
            }

            if (node.From is null)
            {
                EmitPush(node, 0L);
            }
            else
            {
                CompileValue(node.From);
            }

            Emit(node, ExprOpcode.OpSlice);
        }

        private void CompileCall(CallNode node)
        {
            ExprNodeSemantics? callSemantics = Semantics(node);
            ExprNodeSemantics? calleeSemantics = Semantics(node.Callee);
            ExprFunction? function = callSemantics?.Function ?? calleeSemantics?.Function;
            if (function is not null)
            {
                foreach (SyntaxNode argument in node.Arguments)
                {
                    CompileValue(argument);
                }

                EmitFunctionCall(node, function, node.Arguments.Count);
                return;
            }

            if (optionalChains.Count > 0)
            {
                // Upstream evaluates call arguments before the callee. Spill them so
                // an optional nil-callee jump leaves only nil on the operand stack,
                // then restore the ordinary arguments-before-callee call layout.
                int[] argumentSlots = new int[node.Arguments.Count];
                for (var index = 0; index < node.Arguments.Count; index++)
                {
                    CompileValue(node.Arguments[index]);
                    argumentSlots[index] = variableCount++;
                    Emit(node.Arguments[index], ExprOpcode.OpStore, argumentSlots[index]);
                }

                CompileNode(node.Callee);
                int calleeSlot = variableCount++;
                Emit(node.Callee, ExprOpcode.OpStore, calleeSlot);
                foreach (int argumentSlot in argumentSlots)
                {
                    Emit(node, ExprOpcode.OpLoadVar, argumentSlot);
                }

                Emit(node.Callee, ExprOpcode.OpLoadVar, calleeSlot);
            }
            else
            {
                foreach (SyntaxNode argument in node.Arguments)
                {
                    CompileValue(argument);
                }

                CompileNode(node.Callee);
            }

            bool staticallyChecked = callSemantics?.Member?.Kind is ExprMemberBindingKind.ClrMethod ||
                calleeSemantics?.Member?.Kind is ExprMemberBindingKind.ClrMethod;
            Emit(node, staticallyChecked ? ExprOpcode.OpCallTyped : ExprOpcode.OpCall, node.Arguments.Count);
        }

        private void CompileBuiltin(BuiltinNode node)
        {
            switch (node.Name)
            {
                case "all":
                    CompileQuantifier(node, ExprOpcode.OpJumpIfFalse, negate: false, defaultValue: true);
                    return;
                case "none":
                    CompileQuantifier(node, ExprOpcode.OpJumpIfFalse, negate: true, defaultValue: true);
                    return;
                case "any":
                    CompileQuantifier(node, ExprOpcode.OpJumpIfTrue, negate: false, defaultValue: false);
                    return;
                case "one":
                    CompileOne(node);
                    return;
                case "filter":
                    CompileFilter(node);
                    return;
                case "map":
                    CompileMapBuiltin(node);
                    return;
                case "count":
                    CompileCount(node);
                    return;
                case "sum":
                    CompileSum(node);
                    return;
                case "find":
                    CompileFind(node, backwards: false, indexOnly: false);
                    return;
                case "findIndex":
                    CompileFind(node, backwards: false, indexOnly: true);
                    return;
                case "findLast":
                    CompileFind(node, backwards: true, indexOnly: false);
                    return;
                case "findLastIndex":
                    CompileFind(node, backwards: true, indexOnly: true);
                    return;
                case "groupBy":
                    CompileGroupBy(node);
                    return;
                case "sortBy":
                    CompileSortBy(node);
                    return;
                case "reduce":
                    CompileReduce(node);
                    return;
            }

            foreach (SyntaxNode argument in node.Arguments)
            {
                CompileValue(argument);
            }

            ExprFunction function = ResolveBuiltin(node);
            int functionIndex = AddFunction(function);
            if (IsUncheckedFastBuiltin(node, function))
            {
                // Expr's Fast builtins are unary VM operations even when an
                // unchecked subtree contains more syntactic arguments. All
                // arguments are still evaluated left-to-right and the last is
                // consumed. Remove the otherwise-unobservable Go VM residue so
                // the managed VM retains its strict stack-depth invariant.
                Emit(node, ExprOpcode.OpCallBuiltin1, functionIndex);
                if (node.Arguments.Count > 1)
                {
                    int resultSlot = variableCount++;
                    Emit(node, ExprOpcode.OpStore, resultSlot);
                    for (var index = 1; index < node.Arguments.Count; index++)
                    {
                        Emit(node, ExprOpcode.OpPop);
                    }

                    Emit(node, ExprOpcode.OpLoadVar, resultSlot);
                }

                return;
            }

            if (function.SafeInvoker is not null)
            {
                Emit(node, ExprOpcode.OpLoadFunc, functionIndex);
                Emit(node, ExprOpcode.OpCallSafe, node.Arguments.Count);
            }
            else if (node.Arguments.Count is 1)
            {
                Emit(node, ExprOpcode.OpCallBuiltin1, functionIndex);
            }
            else
            {
                EmitFunctionCall(node, function, node.Arguments.Count);
            }
        }

        private void CompileQuantifier(
            BuiltinNode node,
            ExprOpcode breakOpcode,
            bool negate,
            bool defaultValue)
        {
            CompilePredicateSource(node);
            var loopBreak = 0;
            EmitLoop(node, () =>
            {
                CompileValue(node.Arguments[1]);
                if (negate)
                {
                    Emit(node, ExprOpcode.OpNot);
                }

                loopBreak = Emit(node, breakOpcode, JumpPlaceholder);
                Emit(node, ExprOpcode.OpPop);
            });
            Emit(node, defaultValue ? ExprOpcode.OpTrue : ExprOpcode.OpFalse);
            PatchJump(loopBreak);
            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompileOne(BuiltinNode node)
        {
            CompilePredicateSource(node);
            EmitLoop(node, () =>
            {
                CompileValue(node.Arguments[1]);
                EmitCondition(node, () => Emit(node, ExprOpcode.OpIncrementCount));
            });
            Emit(node, ExprOpcode.OpGetCount);
            EmitPush(node, 1L);
            Emit(node, ExprOpcode.OpEqual);
            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompileFilter(BuiltinNode node)
        {
            CompilePredicateSource(node);
            EmitLoop(node, () =>
            {
                CompileValue(node.Arguments[1]);
                EmitCondition(node, () =>
                {
                    Emit(node, ExprOpcode.OpIncrementCount);
                    if (node.Map is null)
                    {
                        Emit(node, ExprOpcode.OpPointer);
                    }
                    else
                    {
                        CompileValue(node.Map);
                    }
                });
            });
            Emit(node, ExprOpcode.OpGetCount);
            Emit(node, ExprOpcode.OpEnd);
            Emit(node, ExprOpcode.OpArray);
        }

        private void CompileMapBuiltin(BuiltinNode node)
        {
            CompilePredicateSource(node);
            EmitLoop(node, () => CompileValue(node.Arguments[1]));
            Emit(node, ExprOpcode.OpGetLen);
            Emit(node, ExprOpcode.OpEnd);
            Emit(node, ExprOpcode.OpArray);
        }

        private void CompileCount(BuiltinNode node)
        {
            CompilePredicateSource(node);
            var loopBreak = 0;
            EmitLoop(node, () =>
            {
                if (node.Arguments.Count is 2)
                {
                    CompileValue(node.Arguments[1]);
                }
                else
                {
                    Emit(node, ExprOpcode.OpPointer);
                }

                EmitCondition(node, () =>
                {
                    Emit(node, ExprOpcode.OpIncrementCount);
                    if (node.Threshold is int threshold)
                    {
                        Emit(node, ExprOpcode.OpGetCount);
                        Emit(node, ExprOpcode.OpInt, threshold);
                        Emit(node, ExprOpcode.OpMoreOrEqual);
                        loopBreak = Emit(node, ExprOpcode.OpJumpIfTrue, JumpPlaceholder);
                        Emit(node, ExprOpcode.OpPop);
                    }
                });
            });
            Emit(node, ExprOpcode.OpGetCount);
            if (node.Threshold is not null)
            {
                int end = Emit(node, ExprOpcode.OpJump, JumpPlaceholder);
                PatchJump(loopBreak);
                Emit(node, ExprOpcode.OpPop);
                Emit(node, ExprOpcode.OpGetCount);
                PatchJump(end);
            }

            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompileSum(BuiltinNode node)
        {
            CompilePredicateSource(node);
            Emit(node, ExprOpcode.OpInt, 0);
            Emit(node, ExprOpcode.OpSetAcc);
            EmitLoop(node, () =>
            {
                if (node.Arguments.Count is 2)
                {
                    CompileValue(node.Arguments[1]);
                }
                else
                {
                    Emit(node, ExprOpcode.OpPointer);
                }

                Emit(node, ExprOpcode.OpGetAcc);
                Emit(node, ExprOpcode.OpAdd);
                Emit(node, ExprOpcode.OpSetAcc);
            });
            Emit(node, ExprOpcode.OpGetAcc);
            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompileFind(BuiltinNode node, bool backwards, bool indexOnly)
        {
            CompilePredicateSource(node);
            var loopBreak = 0;
            void CompileBody()
            {
                CompileValue(node.Arguments[1]);
                int noMatch = Emit(node, ExprOpcode.OpJumpIfFalse, JumpPlaceholder);
                Emit(node, ExprOpcode.OpPop);
                if (indexOnly)
                {
                    Emit(node, ExprOpcode.OpGetIndex);
                }
                else if (node.Map is null)
                {
                    Emit(node, ExprOpcode.OpPointer);
                }
                else
                {
                    CompileValue(node.Map);
                }

                loopBreak = Emit(node, ExprOpcode.OpJump, JumpPlaceholder);
                PatchJump(noMatch);
                Emit(node, ExprOpcode.OpPop);
            }
            if (backwards)
            {
                EmitLoopBackwards(node, CompileBody);
            }
            else
            {
                EmitLoop(node, CompileBody);
            }

            if (!indexOnly && node.Throws)
            {
                EmitPush(node, new ExprErrorOperand("reflect: slice index out of range"));
                Emit(node, ExprOpcode.OpThrow);
            }
            else
            {
                Emit(node, ExprOpcode.OpNil);
            }

            PatchJump(loopBreak);
            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompileGroupBy(BuiltinNode node)
        {
            CompilePredicateSource(node);
            Emit(node, ExprOpcode.OpCreate, 1);
            Emit(node, ExprOpcode.OpSetAcc);
            EmitLoop(node, () =>
            {
                CompileValue(node.Arguments[1]);
                Emit(node, ExprOpcode.OpGroupBy);
            });
            Emit(node, ExprOpcode.OpGetAcc);
            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompileSortBy(BuiltinNode node)
        {
            CompilePredicateSource(node);
            if (node.Arguments.Count is 3)
            {
                CompileValue(node.Arguments[2]);
            }
            else
            {
                EmitPush(node, "asc");
            }

            Emit(node, ExprOpcode.OpCreate, 2);
            Emit(node, ExprOpcode.OpSetAcc);
            EmitLoop(node, () =>
            {
                CompileValue(node.Arguments[1]);
                Emit(node, ExprOpcode.OpSortBy);
            });
            Emit(node, ExprOpcode.OpSort);
            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompileReduce(BuiltinNode node)
        {
            CompilePredicateSource(node);
            if (node.Arguments.Count is 3)
            {
                CompileValue(node.Arguments[2]);
                Emit(node, ExprOpcode.OpSetAcc);
            }
            else
            {
                int empty = Emit(node, ExprOpcode.OpJumpIfEnd, JumpPlaceholder);
                Emit(node, ExprOpcode.OpPointer);
                Emit(node, ExprOpcode.OpIncrementIndex);
                Emit(node, ExprOpcode.OpSetAcc);
                int continueJump = Emit(node, ExprOpcode.OpJump, JumpPlaceholder);
                PatchJump(empty);
                EmitPush(node, new ExprErrorOperand("reduce of empty array with no initial value"));
                Emit(node, ExprOpcode.OpThrow);
                PatchJump(continueJump);
            }

            EmitLoop(node, () =>
            {
                CompileValue(node.Arguments[1]);
                Emit(node, ExprOpcode.OpSetAcc);
            });
            Emit(node, ExprOpcode.OpGetAcc);
            Emit(node, ExprOpcode.OpEnd);
        }

        private void CompilePredicateSource(BuiltinNode node)
        {
            CompileValue(node.Arguments[0]);
            Emit(node, ExprOpcode.OpBegin);
        }

        private void EmitCondition(SyntaxNode node, Action body)
        {
            int noOperation = Emit(node, ExprOpcode.OpJumpIfFalse, JumpPlaceholder);
            Emit(node, ExprOpcode.OpPop);
            body();
            int end = Emit(node, ExprOpcode.OpJump, JumpPlaceholder);
            PatchJump(noOperation);
            Emit(node, ExprOpcode.OpPop);
            PatchJump(end);
        }

        private void EmitLoop(SyntaxNode node, Action body)
        {
            int begin = instructions.Count;
            int end = Emit(node, ExprOpcode.OpJumpIfEnd, JumpPlaceholder);
            body();
            Emit(node, ExprOpcode.OpIncrementIndex);
            Emit(node, ExprOpcode.OpJumpBackward, instructions.Count + 1 - begin);
            PatchJump(end);
        }

        private void EmitLoopBackwards(SyntaxNode node, Action body)
        {
            Emit(node, ExprOpcode.OpGetLen);
            Emit(node, ExprOpcode.OpInt, 1);
            Emit(node, ExprOpcode.OpSubtract);
            Emit(node, ExprOpcode.OpSetIndex);
            int begin = instructions.Count;
            Emit(node, ExprOpcode.OpGetIndex);
            Emit(node, ExprOpcode.OpInt, 0);
            Emit(node, ExprOpcode.OpMoreOrEqual);
            int end = Emit(node, ExprOpcode.OpJumpIfFalse, JumpPlaceholder);
            Emit(node, ExprOpcode.OpPop);
            body();
            Emit(node, ExprOpcode.OpDecrementIndex);
            Emit(node, ExprOpcode.OpJumpBackward, instructions.Count + 1 - begin);
            PatchJump(end);
            Emit(node, ExprOpcode.OpPop);
        }

        private void CompilePointer(PointerNode node)
        {
            Emit(node, node.Name switch
            {
                "" => ExprOpcode.OpPointer,
                "index" => ExprOpcode.OpGetIndex,
                "acc" => ExprOpcode.OpGetAcc,
                _ => throw new ExprCompilationException($"unknown pointer {node.Name}", node.Location),
            });
        }

        private void CompileVariable(VariableDeclaratorNode node)
        {
            CompileValue(node.Value);
            int slot = variableCount++;
            variableNames.Add(slot, node.Name);
            Emit(node, ExprOpcode.OpStore, slot);
            variables.Add(new VariableScope(node.Name, slot));
            try
            {
                CompileValue(node.Body);
            }
            finally
            {
                variables.RemoveAt(variables.Count - 1);
            }
        }

        private void CompileSequence(SequenceNode node)
        {
            for (var index = 0; index < node.Expressions.Count; index++)
            {
                CompileValue(node.Expressions[index]);
                if (index < node.Expressions.Count - 1)
                {
                    Emit(node.Expressions[index], ExprOpcode.OpPop);
                }
            }
        }

        private void CompileConditional(ConditionalNode node)
        {
            CompileValue(node.Condition);
            int otherwise = Emit(node, ExprOpcode.OpJumpIfFalse, JumpPlaceholder);
            Emit(node, ExprOpcode.OpPop);
            CompileValue(node.WhenTrue);
            int end = Emit(node, ExprOpcode.OpJump, JumpPlaceholder);
            PatchJump(otherwise);
            Emit(node, ExprOpcode.OpPop);
            CompileValue(node.WhenFalse);
            PatchJump(end);
        }

        private void CompileArray(ArrayNode node)
        {
            foreach (SyntaxNode element in node.Elements)
            {
                CompileValue(element);
            }

            EmitPush(node, (long)node.Elements.Count);
            Emit(node, ExprOpcode.OpArray);
        }

        private void CompileMap(MapNode node)
        {
            foreach (PairNode pair in node.Pairs)
            {
                CompileNode(pair);
            }

            EmitPush(node, (long)node.Pairs.Count);
            Emit(node, ExprOpcode.OpMap);
        }

        private void CompileValue(SyntaxNode node)
        {
            CompileNode(node);
            if (Semantics(node)?.ValueConversion is not null)
            {
                Emit(node, ExprOpcode.OpDeref);
            }
        }

        private ExprFunction ResolveBuiltin(BuiltinNode node)
        {
            if (Semantics(node)?.Function is ExprFunction checkedFunction)
            {
                return checkedFunction;
            }

            if (configuration.Functions.TryGetValue(node.Name, out ExprFunction? overridden))
            {
                return overridden;
            }

            if (!configuration.DisabledBuiltins.Contains(node.Name) &&
                configuration.Builtins.TryGetValue(node.Name, out ExprFunction? builtin))
            {
                return builtin;
            }

            throw new ExprCompilationException($"unknown builtin {node.Name}", node.Location);
        }

        private bool IsUncheckedFastBuiltin(BuiltinNode node, ExprFunction function)
        {
            if (Semantics(node) is not null ||
                configuration.DisabledBuiltins.Contains(node.Name) ||
                !configuration.Builtins.TryGetValue(node.Name, out ExprFunction? builtin) ||
                !ReferenceEquals(function, builtin))
            {
                return false;
            }

            return node.Name is
                "len" or "type" or "abs" or "ceil" or "floor" or "round" or
                "int" or "float" or "string" or "upper" or "lower";
        }

        private void EmitFunctionCall(SyntaxNode node, ExprFunction function, int argumentCount)
        {
            int index = AddFunction(function);
            if (function.SafeInvoker is not null)
            {
                Emit(node, ExprOpcode.OpLoadFunc, index);
                Emit(node, ExprOpcode.OpCallSafe, argumentCount);
                return;
            }

            switch (argumentCount)
            {
                case 0:
                    Emit(node, ExprOpcode.OpCall0, index);
                    break;
                case 1:
                    Emit(node, ExprOpcode.OpCall1, index);
                    break;
                case 2:
                    Emit(node, ExprOpcode.OpCall2, index);
                    break;
                case 3:
                    Emit(node, ExprOpcode.OpCall3, index);
                    break;
                default:
                    Emit(node, ExprOpcode.OpLoadFunc, index);
                    Emit(node, ExprOpcode.OpCallN, argumentCount);
                    break;
            }
        }

        private int AddFunction(ExprFunction function)
        {
            if (functionIndices.TryGetValue(function, out int existing))
            {
                return existing;
            }

            int index = functions.Count;
            functions.Add(function);
            functionIndices.Add(function, index);
            functionNames.Add(index, function.Name);
            return index;
        }

        private int AddConstant(object value)
        {
            object snapshot = value switch
            {
                byte[] bytes => bytes.ToArray(),
                ReadOnlyMemory<byte> bytes => bytes.ToArray(),
                _ => value,
            };
            if (CanDeduplicate(snapshot) && constantIndices.TryGetValue(snapshot, out int existing))
            {
                return existing;
            }

            int index = constants.Count;
            constants.Add(snapshot);
            if (CanDeduplicate(snapshot))
            {
                constantIndices.Add(snapshot, index);
            }

            return index;
        }

        private static bool CanDeduplicate(object value) => value is string or char or bool or
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or
            DateTime or DateTimeOffset or TimeSpan or Guid or ExprProfilePoint;

        private void EmitPush(SyntaxNode node, object value) =>
            Emit(node, ExprOpcode.OpPush, AddConstant(value));

        private int Emit(SyntaxNode node, ExprOpcode opcode, int argument = 0)
        {
            if (instructions.Count >= MaximumInstructionCount)
            {
                throw new ExprCompilationException(
                    $"compiled program exceeds {MaximumInstructionCount} instructions",
                    node.Location);
            }

            instructions.Add(new MutableInstruction(opcode, argument, node.Location));
            return instructions.Count;
        }

        private void PatchJump(int placeholder)
        {
            if (placeholder <= 0 || placeholder > instructions.Count)
            {
                throw new InvalidOperationException("The jump placeholder is outside the instruction stream.");
            }

            instructions[placeholder - 1].Argument = instructions.Count - placeholder;
        }

        private void OptimizeJumps()
        {
            for (var index = 0; index < instructions.Count; index++)
            {
                MutableInstruction instruction = instructions[index];
                if (instruction.Opcode is not (ExprOpcode.OpJumpIfTrue or ExprOpcode.OpJumpIfFalse or
                    ExprOpcode.OpJumpIfNil or ExprOpcode.OpJumpIfNotNil))
                {
                    continue;
                }

                int target = index + instruction.Argument + 1;
                while (target < instructions.Count && instructions[target].Opcode == instruction.Opcode)
                {
                    target += instructions[target].Argument + 1;
                }

                instruction.Argument = target - index - 1;
            }
        }

        private void EmitResultCast(SourceLocation location)
        {
            ExprOpcode opcode = ExprOpcode.OpCast;
            int? cast = configuration.ExpectedType?.Kind switch
            {
                ExprTypeKind.Integer => (int)ExprCastKind.Integer64,
                ExprTypeKind.Float => (int)ExprCastKind.Float64,
                ExprTypeKind.Boolean => (int)ExprCastKind.Boolean,
                _ => null,
            };
            if (cast is int argument)
            {
                Emit(new ConstantNode(null, location), opcode, argument);
            }
        }

        private ExprProfilePoint? BeginProfile(SyntaxNode node)
        {
            if (!options.EnableProfiling)
            {
                return null;
            }

            var point = new ExprProfilePoint(
                profilePoints.Count,
                profileStack.Count is 0 ? null : profileStack.Peek().Id,
                node.GetType().Name,
                node.Location);
            profilePoints.Add(point);
            profileStack.Push(point);
            Emit(node, ExprOpcode.OpProfileStart, AddConstant(point));
            return point;
        }

        private void EndProfile(SyntaxNode node, ExprProfilePoint? point)
        {
            if (point is null)
            {
                return;
            }

            Emit(node, ExprOpcode.OpProfileEnd, AddConstant(point));
            ExprProfilePoint current = profileStack.Pop();
            if (!ReferenceEquals(current, point))
            {
                throw new InvalidOperationException("The profiling scope stack is corrupt.");
            }
        }

        private bool TryFindVariable(string name, out int slot)
        {
            for (var index = variables.Count - 1; index >= 0; index--)
            {
                if (string.Equals(variables[index].Name, name, StringComparison.Ordinal))
                {
                    slot = variables[index].Slot;
                    return true;
                }
            }

            slot = 0;
            return false;
        }

        private bool IsFastEnvironment()
        {
            Type? type = configuration.Environment?.EnvironmentType;
            return type is not null &&
                (typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(type) ||
                 typeof(IDictionary<string, object?>).IsAssignableFrom(type));
        }

        private ExprNodeSemantics? Semantics(SyntaxNode node) =>
            semanticModel.TryGetSemantics(node, out ExprNodeSemantics? semantics) ? semantics : null;

        private ExprMemberOperand MemberOperand(ExprMemberBinding binding)
        {
            ExprEnvironmentMember? environmentMember = null;
            if (binding.Kind is ExprMemberBindingKind.Environment)
            {
                _ = configuration.Environment?.TryGetMember(binding.Name, out environmentMember);
            }

            return new ExprMemberOperand(binding.Name, binding.Kind, binding.Member, environmentMember);
        }

        private static ExprCompilationException UnknownOperator(string value, SourceLocation location) =>
            new($"unknown operator ({value})", location);

        private bool ParentIsNilCoalescing() => nodes.Count > 1 &&
            nodes[^2] is BinaryNode { Operator: "??" };

        private sealed class MutableInstruction(ExprOpcode opcode, int argument, SourceLocation location)
        {
            internal ExprOpcode Opcode { get; } = opcode;

            internal int Argument { get; set; } = argument;

            internal SourceLocation Location { get; } = location;

            internal ExprInstruction ToImmutable() => new(Opcode, Argument, Location);
        }

        private readonly record struct VariableScope(string Name, int Slot);
    }
}
