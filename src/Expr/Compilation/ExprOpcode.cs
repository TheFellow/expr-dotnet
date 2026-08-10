namespace Expr.Compilation;

/// <summary>Identifies one Expr virtual-machine instruction.</summary>
/// <remarks>
/// Values intentionally follow <c>expr-lang/expr/vm/opcodes.go</c>. Append new
/// instructions immediately before <see cref="OpEnd"/> to retain semport parity.
/// </remarks>
public enum ExprOpcode
{
    /// <summary>An invalid instruction used to detect corrupt programs.</summary>
    OpInvalid,
    /// <summary>Pushes a constant selected by the instruction argument.</summary>
    OpPush,
    /// <summary>Pushes the instruction argument as an integer.</summary>
    OpInt,
    /// <summary>Discards the top stack value.</summary>
    OpPop,
    /// <summary>Stores the top stack value in a local variable.</summary>
    OpStore,
    /// <summary>Loads a local variable.</summary>
    OpLoadVar,
    /// <summary>Loads a dynamically named environment value.</summary>
    OpLoadConst,
    /// <summary>Loads a statically bound environment member.</summary>
    OpLoadField,
    /// <summary>Loads a value from a string-keyed fast environment.</summary>
    OpLoadFast,
    /// <summary>Loads a statically bound environment method.</summary>
    OpLoadMethod,
    /// <summary>Loads a function from the program function table.</summary>
    OpLoadFunc,
    /// <summary>Loads the environment itself.</summary>
    OpLoadEnv,
    /// <summary>Fetches a dynamically selected member or collection element.</summary>
    OpFetch,
    /// <summary>Fetches a statically bound member.</summary>
    OpFetchField,
    /// <summary>Binds a statically selected instance method.</summary>
    OpMethod,
    /// <summary>Pushes <see langword="true"/>.</summary>
    OpTrue,
    /// <summary>Pushes <see langword="false"/>.</summary>
    OpFalse,
    /// <summary>Pushes an Expr nil value.</summary>
    OpNil,
    /// <summary>Numerically negates the top value.</summary>
    OpNegate,
    /// <summary>Logically negates the top value.</summary>
    OpNot,
    /// <summary>Compares two values for Expr equality.</summary>
    OpEqual,
    /// <summary>Compares two canonical integers.</summary>
    OpEqualInt,
    /// <summary>Compares two strings.</summary>
    OpEqualString,
    /// <summary>Jumps forward unconditionally.</summary>
    OpJump,
    /// <summary>Jumps forward when the current value is true.</summary>
    OpJumpIfTrue,
    /// <summary>Jumps forward when the current value is false.</summary>
    OpJumpIfFalse,
    /// <summary>Jumps forward when the current value is nil.</summary>
    OpJumpIfNil,
    /// <summary>Jumps forward when the current value is not nil.</summary>
    OpJumpIfNotNil,
    /// <summary>Jumps forward when the current predicate scope is exhausted.</summary>
    OpJumpIfEnd,
    /// <summary>Jumps backward by the supplied distance.</summary>
    OpJumpBackward,
    /// <summary>Tests collection membership.</summary>
    OpIn,
    /// <summary>Tests whether the left value is less than the right value.</summary>
    OpLess,
    /// <summary>Tests whether the left value is greater than the right value.</summary>
    OpMore,
    /// <summary>Tests whether the left value is less than or equal to the right value.</summary>
    OpLessOrEqual,
    /// <summary>Tests whether the left value is greater than or equal to the right value.</summary>
    OpMoreOrEqual,
    /// <summary>Adds two values.</summary>
    OpAdd,
    /// <summary>Subtracts two values.</summary>
    OpSubtract,
    /// <summary>Multiplies two values.</summary>
    OpMultiply,
    /// <summary>Divides two values.</summary>
    OpDivide,
    /// <summary>Computes an integer remainder.</summary>
    OpModulo,
    /// <summary>Raises a number to a power.</summary>
    OpExponent,
    /// <summary>Creates an inclusive integer range.</summary>
    OpRange,
    /// <summary>Matches a value against a dynamic regular expression.</summary>
    OpMatches,
    /// <summary>Matches a value against a compiled constant regular expression.</summary>
    OpMatchesConst,
    /// <summary>Tests string containment.</summary>
    OpContains,
    /// <summary>Tests a string prefix.</summary>
    OpStartsWith,
    /// <summary>Tests a string suffix.</summary>
    OpEndsWith,
    /// <summary>Slices an array or string.</summary>
    OpSlice,
    /// <summary>Calls an arbitrary runtime callable.</summary>
    OpCall,
    /// <summary>Calls a known function with no arguments.</summary>
    OpCall0,
    /// <summary>Calls a known function with one argument.</summary>
    OpCall1,
    /// <summary>Calls a known function with two arguments.</summary>
    OpCall2,
    /// <summary>Calls a known function with three arguments.</summary>
    OpCall3,
    /// <summary>Calls a loaded known function with an arbitrary argument count.</summary>
    OpCallN,
    /// <summary>Calls a loaded allocation-conscious host callable.</summary>
    OpCallFast,
    /// <summary>Calls a loaded resource-accounting function.</summary>
    OpCallSafe,
    /// <summary>Calls a statically checked CLR callable.</summary>
    OpCallTyped,
    /// <summary>Calls a one-argument built-in selected from the function table.</summary>
    OpCallBuiltin1,
    /// <summary>Creates an array from stack values.</summary>
    OpArray,
    /// <summary>Creates a map from stack values.</summary>
    OpMap,
    /// <summary>Pushes the length of the current value.</summary>
    OpLen,
    /// <summary>Casts the top value to a configured result type.</summary>
    OpCast,
    /// <summary>Unwraps a configured host value provider.</summary>
    OpDeref,
    /// <summary>Advances the current predicate index.</summary>
    OpIncrementIndex,
    /// <summary>Moves the current predicate index backward.</summary>
    OpDecrementIndex,
    /// <summary>Increments the current predicate match count.</summary>
    OpIncrementCount,
    /// <summary>Pushes the current predicate index.</summary>
    OpGetIndex,
    /// <summary>Pushes the current predicate match count.</summary>
    OpGetCount,
    /// <summary>Pushes the current predicate collection length.</summary>
    OpGetLen,
    /// <summary>Pushes the current predicate accumulator.</summary>
    OpGetAcc,
    /// <summary>Sets the current predicate accumulator.</summary>
    OpSetAcc,
    /// <summary>Sets the current predicate index.</summary>
    OpSetIndex,
    /// <summary>Pushes the current predicate element.</summary>
    OpPointer,
    /// <summary>Throws the error represented by the top value.</summary>
    OpThrow,
    /// <summary>Creates an internal predicate helper selected by the argument.</summary>
    OpCreate,
    /// <summary>Adds the current element to a group.</summary>
    OpGroupBy,
    /// <summary>Captures a sort key for the current element.</summary>
    OpSortBy,
    /// <summary>Sorts the captured elements.</summary>
    OpSort,
    /// <summary>Begins profiling a syntax node.</summary>
    OpProfileStart,
    /// <summary>Ends profiling a syntax node.</summary>
    OpProfileEnd,
    /// <summary>Begins a predicate scope.</summary>
    OpBegin,
    /// <summary>Combines two Boolean values without short circuiting.</summary>
    OpAnd,
    /// <summary>Combines two Boolean values without short circuiting.</summary>
    OpOr,
    /// <summary>Ends a predicate scope and remains the opcode-list sentinel.</summary>
    OpEnd,
}
