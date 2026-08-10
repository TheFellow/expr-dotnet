using System;

namespace Expr.Runtime;

/// <summary>Controls how a CLR property is exposed to Expr.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ExprMemberAttribute : Attribute
{
    /// <summary>
    /// Initializes an attribute with the CLR property name.
    /// </summary>
    public ExprMemberAttribute()
    {
    }

    /// <summary>
    /// Initializes an attribute with an expression-visible alias.
    /// </summary>
    /// <param name="name">The expression-visible name.</param>
    public ExprMemberAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the expression-visible alias, or <see langword="null"/> to retain the CLR name.</summary>
    public string? Name { get; }

    /// <summary>Gets or sets a value indicating whether the property is hidden from Expr.</summary>
    public bool Ignore { get; set; }
}
