namespace Expr.Runtime;

/// <summary>Identifies runtime value categories used by Expr operations.</summary>
public enum ExprValueKind
{
    /// <summary>A null value.</summary>
    Nil,

    /// <summary>A Boolean value.</summary>
    Boolean,

    /// <summary>A signed integral value.</summary>
    SignedInteger,

    /// <summary>An unsigned integral value.</summary>
    UnsignedInteger,

    /// <summary>A floating-point value.</summary>
    Float,

    /// <summary>A string value.</summary>
    String,

    /// <summary>An instant in time.</summary>
    Time,

    /// <summary>A duration.</summary>
    Duration,

    /// <summary>An ordered collection.</summary>
    Array,

    /// <summary>A keyed collection.</summary>
    Map,

    /// <summary>A callable value.</summary>
    Function,

    /// <summary>Another host value.</summary>
    Object,
}
