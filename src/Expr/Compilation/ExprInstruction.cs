using Expr.Syntax;

namespace Expr.Compilation;

/// <summary>Represents one immutable instruction and its source range.</summary>
/// <param name="Opcode">The operation to execute.</param>
/// <param name="Argument">The operation-specific integer argument.</param>
/// <param name="Location">The syntax range that emitted the instruction.</param>
public readonly record struct ExprInstruction(
    ExprOpcode Opcode,
    int Argument,
    SourceLocation Location);

/// <summary>Identifies the canonical target of <see cref="ExprOpcode.OpCast"/>.</summary>
public enum ExprCastKind
{
    /// <summary>A platform-sized integer, retained for Go bytecode compatibility.</summary>
    Integer,
    /// <summary>The canonical signed 64-bit Expr integer used by .NET.</summary>
    Integer64,
    /// <summary>A double-precision floating-point value.</summary>
    Float64,
    /// <summary>A Boolean value.</summary>
    Boolean,
}
