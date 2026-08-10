using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Expr.Compilation;
using Expr.Patching;
using Expr.Runtime;

namespace Expr.Execution;

internal sealed class ExprExecutionMachine
{
    private readonly ExprProgram program;
    private readonly object? environment;
    private readonly ExprEvaluationOptions options;
    private readonly CancellationToken cancellationToken;
    private readonly List<object?> stack = [];
    private readonly object?[] variables;
    private List<PredicateScope>? scopes;
    private Dictionary<int, MutableProfile>? profiles;
    private Stack<ActiveProfile>? activeProfiles;
    private ulong memoryUsed;
    private ulong workUsed;
    private int instructionPointer;

    internal ExprExecutionMachine(
        ExprProgram program,
        object? environment,
        ExprEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        this.program = program;
        this.environment = environment;
        this.options = options;
        this.cancellationToken = cancellationToken;
        variables = new object?[program.VariableCount];
    }

    internal object? RunValue() => Execute();

    internal ExprEvaluationResult RunDetailed()
    {
        object? value = Execute();
        IEnumerable<ExprProfileSample> samples = profiles is null
            ? []
            : profiles.Values
                .OrderBy(static profile => profile.Point.Id)
                .Select(static profile => new ExprProfileSample(
                    profile.Point,
                    TimeSpan.FromTicks(profile.ElapsedTicks),
                    profile.InvocationCount));
        return new ExprEvaluationResult(value, memoryUsed, workUsed, samples);
    }

    private object? Execute()
    {
        try
        {
            while (instructionPointer < program.Instructions.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChargeWork();
                int currentIndex = instructionPointer;
                ExprInstruction instruction = program.Instructions[instructionPointer++];
                Dispatch(currentIndex, instruction);
            }

            if (scopes?.Count is > 0)
            {
                throw Error("program ended with an open predicate scope");
            }

            if (activeProfiles?.Count is > 0)
            {
                throw Error("program ended with an open profile scope");
            }

            if (stack.Count is not 1)
            {
                throw Error(
                    $"program ended with an invalid stack depth of {stack.Count.ToString(CultureInfo.InvariantCulture)}");
            }

            return Pop();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ExprExecutionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            int failedIndex = Math.Clamp(instructionPointer - 1, 0, program.Instructions.Count - 1);
            throw new ExprExecutionException(
                exception.Message,
                failedIndex,
                program.Instructions[failedIndex].Location,
                program.Source,
                exception);
        }
    }

    private void Dispatch(int currentIndex, ExprInstruction instruction)
    {
        int argument = instruction.Argument;
        switch (instruction.Opcode)
        {
            case ExprOpcode.OpInvalid:
                throw Error("invalid opcode");
            case ExprOpcode.OpPush:
                Push(program.Constants[argument]);
                break;
            case ExprOpcode.OpInt:
                Push((long)argument);
                break;
            case ExprOpcode.OpPop:
                _ = Pop();
                break;
            case ExprOpcode.OpStore:
                variables[argument] = Pop();
                break;
            case ExprOpcode.OpLoadVar:
                Push(variables[argument]);
                break;
            case ExprOpcode.OpLoadConst:
                Push(ExprExecutionOperations.FetchEnvironment(environment, Constant<string>(argument)));
                break;
            case ExprOpcode.OpLoadField:
                Push(ExprExecutionOperations.FetchBound(environment, Constant<ExprMemberOperand>(argument)));
                break;
            case ExprOpcode.OpLoadFast:
                Push(ExprExecutionOperations.FetchEnvironment(environment, Constant<string>(argument)));
                break;
            case ExprOpcode.OpLoadMethod:
                Push(ExprExecutionOperations.FetchBound(environment, Constant<ExprMemberOperand>(argument)));
                break;
            case ExprOpcode.OpLoadFunc:
                Push(program.Functions[argument]);
                break;
            case ExprOpcode.OpLoadEnv:
                Push(environment);
                break;
            case ExprOpcode.OpFetch:
                {
                    object? key = Pop();
                    object? target = Pop();
                    Push(ExprExecutionOperations.Fetch(target, key));
                    break;
                }

            case ExprOpcode.OpFetchField:
                Push(ExprExecutionOperations.FetchBound(Pop(), Constant<ExprMemberOperand>(argument)));
                break;
            case ExprOpcode.OpMethod:
                Push(ExprExecutionOperations.FetchBound(Pop(), Constant<ExprMemberOperand>(argument)));
                break;
            case ExprOpcode.OpTrue:
                Push(true);
                break;
            case ExprOpcode.OpFalse:
                Push(false);
                break;
            case ExprOpcode.OpNil:
                Push(null);
                break;
            case ExprOpcode.OpNegate:
                Push(ExprExecutionOperations.Negate(Pop()));
                break;
            case ExprOpcode.OpNot:
                Push(!RequireBoolean(Pop()));
                break;
            case ExprOpcode.OpEqual:
                {
                    object? right = Pop();
                    object? left = Pop();
                    Push(Equal(left, right));
                    break;
                }

            case ExprOpcode.OpEqualInt:
                {
                    long right = RequireInteger(Pop());
                    Push(RequireInteger(Pop()) == right);
                    break;
                }

            case ExprOpcode.OpEqualString:
                {
                    string right = RequireString(Pop());
                    Push(string.Equals(RequireString(Pop()), right, StringComparison.Ordinal));
                    break;
                }

            case ExprOpcode.OpJump:
                instructionPointer = ForwardTarget(currentIndex, argument);
                break;
            case ExprOpcode.OpJumpIfTrue:
                if (RequireBoolean(Current()))
                {
                    instructionPointer = ForwardTarget(currentIndex, argument);
                }

                break;
            case ExprOpcode.OpJumpIfFalse:
                if (!RequireBoolean(Current()))
                {
                    instructionPointer = ForwardTarget(currentIndex, argument);
                }

                break;
            case ExprOpcode.OpJumpIfNil:
                if (ExprCollections.IsNil(Current()))
                {
                    instructionPointer = ForwardTarget(currentIndex, argument);
                }

                break;
            case ExprOpcode.OpJumpIfNotNil:
                if (!ExprCollections.IsNil(Current()))
                {
                    instructionPointer = ForwardTarget(currentIndex, argument);
                }

                break;
            case ExprOpcode.OpJumpIfEnd:
                if (CurrentScope().Index >= CurrentScope().Length)
                {
                    instructionPointer = ForwardTarget(currentIndex, argument);
                }

                break;
            case ExprOpcode.OpJumpBackward:
                cancellationToken.ThrowIfCancellationRequested();
                instructionPointer = currentIndex + 1 - argument;
                break;
            case ExprOpcode.OpIn:
                {
                    object? collection = Pop();
                    object? needle = Pop();
                    Push(In(needle, collection));
                    break;
                }

            case ExprOpcode.OpLess:
                Compare(static (left, right) => ExprValue.Less(left, right));
                break;
            case ExprOpcode.OpMore:
                Compare(static (left, right) => ExprValue.Less(right, left));
                break;
            case ExprOpcode.OpLessOrEqual:
                Compare(static (left, right) =>
                    ExprValue.Equal(left, right) || ExprValue.Less(left, right));
                break;
            case ExprOpcode.OpMoreOrEqual:
                Compare(static (left, right) =>
                    ExprValue.Equal(left, right) || ExprValue.Less(right, left));
                break;
            case ExprOpcode.OpAdd:
                ExecuteAdd();
                break;
            case ExprOpcode.OpSubtract:
                Binary(ExprExecutionOperations.Subtract);
                break;
            case ExprOpcode.OpMultiply:
                Binary(ExprExecutionOperations.Multiply);
                break;
            case ExprOpcode.OpDivide:
                Binary(static (left, right) => ExprExecutionOperations.Divide(left, right));
                break;
            case ExprOpcode.OpModulo:
                Binary(static (left, right) => ExprExecutionOperations.Modulo(left, right));
                break;
            case ExprOpcode.OpExponent:
                Binary(static (left, right) => Math.Pow(ExprValue.ToDouble(left), ExprValue.ToDouble(right)));
                break;
            case ExprOpcode.OpRange:
                ExecuteRange();
                break;
            case ExprOpcode.OpMatches:
                {
                    object? pattern = Pop();
                    Push(ExprExecutionOperations.DynamicMatch(Pop(), pattern, options));
                    break;
                }

            case ExprOpcode.OpMatchesConst:
                ExecuteConstantMatch(Constant<ExprRegularExpressionOperand>(argument));
                break;
            case ExprOpcode.OpContains:
                StringOperation(static (left, right) => left.Contains(right, StringComparison.Ordinal));
                break;
            case ExprOpcode.OpStartsWith:
                StringOperation(static (left, right) => left.StartsWith(right, StringComparison.Ordinal));
                break;
            case ExprOpcode.OpEndsWith:
                StringOperation(static (left, right) => left.EndsWith(right, StringComparison.Ordinal));
                break;
            case ExprOpcode.OpSlice:
                {
                    object? from = Pop();
                    object? to = Pop();
                    object? target = Pop();
                    ChargeMemory(ExprExecutionOperations.SliceAllocationCost(target, from, to));
                    Push(ExprExecutionOperations.Slice(target, from, to));
                    break;
                }

            case ExprOpcode.OpCall:
            case ExprOpcode.OpCallN:
            case ExprOpcode.OpCallFast:
            case ExprOpcode.OpCallTyped:
                ExecuteCall(argument, safe: false);
                break;
            case ExprOpcode.OpCallSafe:
                ExecuteCall(argument, safe: true);
                break;
            case ExprOpcode.OpCall0:
                ExecuteKnownCall(argument, 0);
                break;
            case ExprOpcode.OpCall1:
                ExecuteKnownCall(argument, 1);
                break;
            case ExprOpcode.OpCall2:
                ExecuteKnownCall(argument, 2);
                break;
            case ExprOpcode.OpCall3:
                ExecuteKnownCall(argument, 3);
                break;
            case ExprOpcode.OpCallBuiltin1:
                ExecuteKnownCall(argument, 1);
                break;
            case ExprOpcode.OpArray:
                ExecuteArray();
                break;
            case ExprOpcode.OpMap:
                ExecuteMap();
                break;
            case ExprOpcode.OpLen:
                Push((long)StorageLength(Current()));
                break;
            case ExprOpcode.OpCast:
                Push(ExprExecutionOperations.Cast(Pop(), (ExprCastKind)argument));
                break;
            case ExprOpcode.OpDeref:
                {
                    object? value = Pop();
                    Push(value is IExprValueProvider provider ? provider.ToExprValue() : value);
                    break;
                }

            case ExprOpcode.OpIncrementIndex:
                CurrentScope().Index++;
                break;
            case ExprOpcode.OpDecrementIndex:
                CurrentScope().Index--;
                break;
            case ExprOpcode.OpIncrementCount:
                CurrentScope().Count++;
                break;
            case ExprOpcode.OpGetIndex:
                Push((long)CurrentScope().Index);
                break;
            case ExprOpcode.OpGetCount:
                Push((long)CurrentScope().Count);
                break;
            case ExprOpcode.OpGetLen:
                Push((long)CurrentScope().Length);
                break;
            case ExprOpcode.OpGetAcc:
                Push(PublicAccumulator(CurrentScope().Accumulator));
                break;
            case ExprOpcode.OpSetAcc:
                CurrentScope().Accumulator = Pop();
                break;
            case ExprOpcode.OpSetIndex:
                CurrentScope().Index = CheckedInt(RequireInteger(Pop()), "predicate index");
                break;
            case ExprOpcode.OpPointer:
                Push(CurrentScope().Item());
                break;
            case ExprOpcode.OpThrow:
                ThrowValue(Pop());
                break;
            case ExprOpcode.OpCreate:
                ExecuteCreate(argument);
                break;
            case ExprOpcode.OpGroupBy:
                ExecuteGroupBy();
                break;
            case ExprOpcode.OpSortBy:
                ExecuteSortBy();
                break;
            case ExprOpcode.OpSort:
                ExecuteSort();
                break;
            case ExprOpcode.OpProfileStart:
                ProfileStart(Constant<ExprProfilePoint>(argument));
                break;
            case ExprOpcode.OpProfileEnd:
                ProfileEnd(Constant<ExprProfilePoint>(argument));
                break;
            case ExprOpcode.OpBegin:
                BeginScope(Pop());
                break;
            case ExprOpcode.OpAnd:
                {
                    bool right = RequireBoolean(Pop());
                    Push(RequireBoolean(Pop()) && right);
                    break;
                }

            case ExprOpcode.OpOr:
                {
                    bool right = RequireBoolean(Pop());
                    Push(RequireBoolean(Pop()) || right);
                    break;
                }

            case ExprOpcode.OpEnd:
                EndScope();
                break;
            default:
                throw Error("unknown opcode");
        }
    }

    private void ExecuteRange()
    {
        long maximum = ExprValue.ToInt64(Pop());
        long minimum = ExprValue.ToInt64(Pop());
        ulong size = maximum < minimum ? 0 : checked((ulong)(maximum - minimum) + 1);
        if (size > (ulong)options.MaximumCollectionLength || size > int.MaxValue)
        {
            throw Error("range exceeds the configured collection limit");
        }

        ChargeMemory(size);
        var values = new object?[(int)size];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = unchecked(minimum + index);
        }

        Push(new ExprArray(values));
    }

    private void ExecuteAdd()
    {
        object? right = Pop();
        object? left = Pop();
        if (left is string leftText && right is string rightText)
        {
            ulong cost = checked(
                (ulong)Encoding.UTF8.GetByteCount(leftText) +
                (ulong)Encoding.UTF8.GetByteCount(rightText));
            ChargeMemory(cost);
        }

        Push(ExprExecutionOperations.Add(left, right));
    }

    private void ExecuteConstantMatch(ExprRegularExpressionOperand expression)
    {
        object? input = Pop();
        if (input is null)
        {
            Push(false);
            return;
        }

        string text = input switch
        {
            string value => value,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            _ => throw Error($"invalid regular expression input type {input.GetType().FullName}"),
        };
        Push(expression.CompiledExpression.IsMatch(text));
    }

    private void ExecuteCall(int argumentCount, bool safe)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object? callable = Pop();
        object?[] arguments = PopArguments(argumentCount);
        if (safe)
        {
            if (callable is not ExprFunction function)
            {
                throw Error("safe call target is not an Expr function");
            }

            EnsureMemoryAvailable(function.EstimateMemoryCost(arguments));
            ExprInvocationResult result = function.Invoke(arguments);
            ChargeMemory(result.MemoryCost);
            Push(result.Value);
            return;
        }

        ExprInvocationResult invocation = ExprExecutionOperations.Invoke(callable, arguments);
        cancellationToken.ThrowIfCancellationRequested();
        ChargeMemory(invocation.MemoryCost);
        Push(invocation.Value);
    }

    private void ExecuteKnownCall(int functionIndex, int argumentCount)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object?[] arguments = PopArguments(argumentCount);
        ExprFunction function = program.Functions[functionIndex];
        EnsureMemoryAvailable(function.EstimateMemoryCost(arguments));
        ExprInvocationResult result = function.Invoke(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        ChargeMemory(result.MemoryCost);
        Push(result.Value);
    }

    private void ExecuteArray()
    {
        int size = CheckedCollectionSize(Pop());
        RequireStack(size);
        ChargeMemory((ulong)size);
        object?[] values = PopArguments(size);
        Push(new ExprArray(values));
    }

    private void ExecuteMap()
    {
        int size = CheckedCollectionSize(Pop());
        RequireStack(checked(size * 2));
        ChargeMemory((ulong)size);
        var entries = new List<KeyValuePair<object?, object?>>(size);
        object?[] values = PopArguments(checked(size * 2));
        // Upstream pops map pairs from the stack in reverse source order. Processing
        // from the end preserves its intentional "first duplicate key wins" rule.
        for (var index = values.Length - 2; index >= 0; index -= 2)
        {
            object? keyValue = values[index];
            if (keyValue is not string key)
            {
                throw Error($"cannot use {keyValue?.GetType().FullName ?? "nil"} as a map key");
            }

            int existing = entries.FindIndex(entry => ExprValue.Equal(entry.Key, key));
            var pair = new KeyValuePair<object?, object?>(key, values[index + 1]);
            if (existing >= 0)
            {
                entries[existing] = pair;
            }
            else
            {
                entries.Add(pair);
            }
        }

        Push(new ExprMap(entries));
    }

    private void ExecuteCreate(int kind)
    {
        if (kind is 1)
        {
            Push(new GroupAccumulator());
            return;
        }

        string order = RequireString(Pop());
        bool descending = order switch
        {
            "asc" => false,
            "desc" => true,
            _ => throw Error("unknown order, use asc or desc"),
        };
        Push(new SortAccumulator(descending, CurrentScope().Length));
    }

    private void ExecuteGroupBy()
    {
        PredicateScope scope = CurrentScope();
        object? key = Pop();
        if (scope.Accumulator is not GroupAccumulator accumulator)
        {
            throw Error("groupBy accumulator is corrupt");
        }

        accumulator.Add(key, scope.Item());
    }

    private void ExecuteSortBy()
    {
        PredicateScope scope = CurrentScope();
        object? key = Pop();
        if (scope.Accumulator is not SortAccumulator accumulator)
        {
            throw Error("sortBy accumulator is corrupt");
        }

        accumulator.Add(scope.Item(), key);
    }

    private void ExecuteSort()
    {
        if (CurrentScope().Accumulator is not SortAccumulator accumulator)
        {
            throw Error("sortBy accumulator is corrupt");
        }

        ChargeMemory((ulong)accumulator.Count);
        Push(accumulator.Sort());
    }

    private void BeginScope(object? source)
    {
        scopes ??= [];
        if (scopes.Count >= options.MaximumScopeDepth)
        {
            throw Error("predicate scope depth exceeded");
        }

        IExprArray array;
        if (ExprCollections.TryAsArray(source, out IExprArray? adapted) && adapted is not null)
        {
            array = adapted;
        }
        else if (ExprExecutionOperations.TryGetBytes(source, out ReadOnlyMemory<byte> bytes))
        {
            array = new ExprArray(bytes.ToArray().Select(static value => (object?)value));
        }
        else if (source is string text)
        {
            array = new ExprArray(Encoding.UTF8.GetBytes(text).Select(static value => (object?)value));
        }
        else if (ExprCollections.TryAsMap(source, out IExprMap? map) && map is not null)
        {
            // Pinned Expr's VM accepts a map scope and its length, but reflect.Index on
            // the map panics if the predicate actually reads #. Preserve that behavior.
            array = new MapPredicateSource(map.Count);
        }
        else
        {
            throw Error($"cannot iterate {source?.GetType().FullName ?? "nil"}");
        }

        if (array.Count > options.MaximumCollectionLength)
        {
            throw Error("predicate source exceeds the configured collection limit");
        }

        scopes.Add(new PredicateScope(array));
    }

    private void EndScope()
    {
        if (scopes is null || scopes.Count is 0)
        {
            throw Error("predicate scope underflow");
        }

        scopes.RemoveAt(scopes.Count - 1);
    }

    private void ProfileStart(ExprProfilePoint point)
    {
        activeProfiles ??= new Stack<ActiveProfile>();
        if (activeProfiles.Count >= options.MaximumScopeDepth)
        {
            throw Error("profile scope depth exceeded");
        }

        activeProfiles.Push(new ActiveProfile(
            point,
            options.EnableProfiling ? Stopwatch.GetTimestamp() : 0));
    }

    private void ProfileEnd(ExprProfilePoint point)
    {
        if (activeProfiles is null ||
            !activeProfiles.TryPop(out ActiveProfile active) ||
            active.Point.Id != point.Id)
        {
            throw Error("profile scope is corrupt");
        }

        if (!options.EnableProfiling)
        {
            return;
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(active.StartTimestamp);
        profiles ??= new Dictionary<int, MutableProfile>();
        if (!profiles.TryGetValue(point.Id, out MutableProfile? profile))
        {
            profile = new MutableProfile(point);
            profiles.Add(point.Id, profile);
        }

        profile.ElapsedTicks = checked(profile.ElapsedTicks + elapsed.Ticks);
        profile.InvocationCount++;
    }

    private void StringOperation(Func<string, string, bool> operation)
    {
        object? rightValue = Pop();
        object? leftValue = Pop();
        if (leftValue is null || rightValue is null)
        {
            Push(false);
            return;
        }

        Push(operation(RequireString(leftValue), RequireString(rightValue)));
    }

    private void Binary(Func<object?, object?, object?> operation)
    {
        object? right = Pop();
        Push(operation(Pop(), right));
    }

    private void Compare(Func<object?, object?, bool> operation)
    {
        object? right = Pop();
        Push(operation(Pop(), right));
    }

    private object?[] PopArguments(int count)
    {
        RequireStack(count);
        int offset = stack.Count - count;
        var arguments = new object?[count];
        stack.CopyTo(offset, arguments, 0, count);
        stack.RemoveRange(offset, count);
        return arguments;
    }

    private void Push(object? value)
    {
        if (stack.Count >= options.MaximumStackDepth)
        {
            throw Error("stack depth exceeded");
        }

        stack.Add(value);
    }

    private object? Pop()
    {
        RequireStack(1);
        int index = stack.Count - 1;
        object? value = stack[index];
        stack.RemoveAt(index);
        return value;
    }

    private object? Current()
    {
        RequireStack(1);
        return stack[^1];
    }

    private void RequireStack(int count)
    {
        if (count < 0 || stack.Count < count)
        {
            throw Error("stack underflow");
        }
    }

    private PredicateScope CurrentScope()
    {
        if (scopes is null || scopes.Count is 0)
        {
            throw Error("predicate scope underflow");
        }

        return scopes[^1];
    }

    private void ChargeMemory(ulong amount)
    {
        if (amount is 0)
        {
            return;
        }

        EnsureMemoryAvailable(amount);

        memoryUsed = checked(memoryUsed + amount);
    }

    private void EnsureMemoryAvailable(ulong amount)
    {
        if (amount is not 0 &&
            options.MemoryBudget is not 0 &&
            (amount >= options.MemoryBudget || memoryUsed >= options.MemoryBudget - amount))
        {
            throw Error("memory budget exceeded");
        }
    }

    private void ChargeWork()
    {
        if (workUsed >= options.WorkBudget)
        {
            throw Error("work budget exceeded");
        }

        workUsed++;
    }

    private T Constant<T>(int index) where T : class =>
        program.Constants[index] as T ?? throw Error("constant operand has an invalid type");

    private int CheckedCollectionSize(object? value)
    {
        long requested = RequireInteger(value);
        if (requested < 0 || requested > options.MaximumCollectionLength || requested > int.MaxValue)
        {
            throw Error("collection size exceeds the configured limit");
        }

        return (int)requested;
    }

    private static int CheckedInt(long value, string name)
    {
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw Error($"{name} is out of range");
        }

        return (int)value;
    }

    private static bool RequireBoolean(object? value) => value is bool boolean
        ? boolean
        : throw Error($"invalid operation: bool({value?.GetType().FullName ?? "nil"})");

    private static string RequireString(object? value) => value as string ??
        throw Error($"expected string, got {value?.GetType().FullName ?? "nil"}");

    private static long RequireInteger(object? value) => value switch
    {
        null => throw Error("expected integer, got nil"),
        sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint =>
            ExprValue.ToInt64(value),
        _ => throw Error($"expected integer, got {value.GetType().FullName}"),
    };

    private static int ForwardTarget(int currentIndex, int argument) => currentIndex + 1 + argument;

    private static bool Equal(object? left, object? right)
    {
        if (ExprExecutionOperations.TryGetBytes(left, out ReadOnlyMemory<byte> leftBytes) &&
            ExprExecutionOperations.TryGetBytes(right, out ReadOnlyMemory<byte> rightBytes))
        {
            return leftBytes.Span.SequenceEqual(rightBytes.Span);
        }

        return ExprValue.Equal(left, right);
    }

    private static bool In(object? needle, object? collection)
    {
        if (ExprExecutionOperations.TryGetBytes(collection, out ReadOnlyMemory<byte> bytes))
        {
            long value = RequireInteger(needle);
            return value is >= byte.MinValue and <= byte.MaxValue && bytes.Span.Contains((byte)value);
        }

        return ExprValue.In(needle, collection);
    }

    private static int StorageLength(object? value) =>
        ExprExecutionOperations.TryGetBytes(value, out ReadOnlyMemory<byte> bytes)
            ? bytes.Length
            : ExprValue.StorageLength(value);

    private static object? PublicAccumulator(object? accumulator) => accumulator switch
    {
        GroupAccumulator group => group.ToMap(),
        _ => accumulator,
    };

    private static void ThrowValue(object? value)
    {
        throw value switch
        {
            null => Error("nil"),
            ExprErrorOperand error => Error(error.Message),
            Exception exception => exception,
            _ => Error(ExprDisplay.Value(value)),
        };
    }

    private static ExprRuntimeException Error(string message) => new(message);

    private sealed class PredicateScope(IExprArray source)
    {
        internal int Index { get; set; }

        internal int Length => source.Count;

        internal long Count { get; set; }

        internal object? Accumulator { get; set; }

        internal object? Item()
        {
            if ((uint)Index >= (uint)source.Count)
            {
                throw Error("predicate index is out of range");
            }

            return source[Index];
        }
    }

    private sealed class MapPredicateSource(int count) : IExprArray
    {
        public Type ElementType => typeof(object);

        public int Count { get; } = count;

        public object? this[int index] => throw Error("cannot index map predicate source");

        public IEnumerator<object?> GetEnumerator() => throw Error("cannot enumerate map predicate source");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class GroupAccumulator
    {
        private readonly List<Group> groups = [];

        internal void Add(object? key, object? value)
        {
            Group? group = groups.FirstOrDefault(candidate => ExprValue.Equal(candidate.Key, key));
            if (group is null)
            {
                group = new Group(key);
                groups.Add(group);
            }

            group.Values.Add(value);
        }

        internal IExprMap ToMap() => new GroupMap(groups.Select(group =>
            new KeyValuePair<object?, object?>(group.Key, new ExprArray(group.Values))));

        private sealed class Group(object? key)
        {
            internal object? Key { get; } = key;

            internal List<object?> Values { get; } = [];
        }

        private sealed class GroupMap(IEnumerable<KeyValuePair<object?, object?>> values) : IExprMap
        {
            private readonly IReadOnlyList<KeyValuePair<object?, object?>> entries =
                Array.AsReadOnly(values.ToArray());

            public Type KeyType => typeof(object);

            public Type ValueType => typeof(IExprArray);

            public int Count => entries.Count;

            public bool TryGetValue(object? key, out object? value)
            {
                foreach ((object? candidate, object? candidateValue) in entries)
                {
                    if (ExprValue.Equal(candidate, key))
                    {
                        value = candidateValue;
                        return true;
                    }
                }

                value = null;
                return false;
            }

            public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator() => entries.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    private sealed class SortAccumulator(bool descending, int capacity)
    {
        private readonly List<SortItem> items = new(capacity);

        internal int Count => items.Count;

        internal void Add(object? value, object? key) => items.Add(new SortItem(value, key, items.Count));

        internal ExprArray Sort()
        {
            items.Sort((left, right) =>
            {
                if (ExprValue.Equal(left.Key, right.Key))
                {
                    return left.Index.CompareTo(right.Index);
                }

                bool leftBefore = descending
                    ? ExprValue.Less(right.Key, left.Key)
                    : ExprValue.Less(left.Key, right.Key);
                if (leftBefore)
                {
                    return -1;
                }

                bool rightBefore = descending
                    ? ExprValue.Less(left.Key, right.Key)
                    : ExprValue.Less(right.Key, left.Key);
                return rightBefore ? 1 : left.Index.CompareTo(right.Index);
            });
            return new ExprArray(items.Select(static item => item.Value));
        }

        private sealed record SortItem(object? Value, object? Key, int Index);
    }

    private readonly record struct ActiveProfile(ExprProfilePoint Point, long StartTimestamp);

    private sealed class MutableProfile(ExprProfilePoint point)
    {
        internal ExprProfilePoint Point { get; } = point;

        internal long ElapsedTicks { get; set; }

        internal long InvocationCount { get; set; }
    }
}
