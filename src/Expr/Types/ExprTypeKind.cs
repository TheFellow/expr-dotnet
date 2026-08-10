namespace Expr.Types;

/// <summary>
/// Identifies the semantic categories understood by the Expr type system.
/// </summary>
public enum ExprTypeKind
{
    /// <summary>The type cannot be determined statically.</summary>
    Unknown,

    /// <summary>The value is <see langword="null"/>.</summary>
    Nil,

    /// <summary>Any value is accepted.</summary>
    Any,

    /// <summary>A Boolean value.</summary>
    Boolean,

    /// <summary>An integral numeric value.</summary>
    Integer,

    /// <summary>A floating-point numeric value.</summary>
    Float,

    /// <summary>A Unicode string value.</summary>
    String,

    /// <summary>An instant in time.</summary>
    Time,

    /// <summary>A duration.</summary>
    Duration,

    /// <summary>An ordered collection.</summary>
    Array,

    /// <summary>A keyed collection.</summary>
    Map,

    /// <summary>A CLR object with named members.</summary>
    Object,

    /// <summary>A callable value.</summary>
    Function,
}
