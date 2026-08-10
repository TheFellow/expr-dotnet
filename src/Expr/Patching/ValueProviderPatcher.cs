using Expr.Checking;
using Expr.Configuration;
using Expr.Syntax;

namespace Expr.Patching;

/// <summary>Provides a standard Expr value from a host-specific wrapper.</summary>
public interface IExprValueProvider
{
    /// <summary>Gets the value visible to expression evaluation.</summary>
    /// <returns>The unwrapped value.</returns>
    object? ToExprValue();
}

/// <summary>Provides a statically typed standard Expr value from a host-specific wrapper.</summary>
/// <typeparam name="TValue">The unwrapped value type used by static checking.</typeparam>
public interface IExprValueProvider<out TValue> : IExprValueProvider
{
    /// <summary>Gets the statically typed value visible to expression evaluation.</summary>
    /// <returns>The unwrapped value.</returns>
    new TValue ToExprValue();

    object? IExprValueProvider.ToExprValue() => ToExprValue();
}

/// <summary>
/// Enables value-provider semantics. The checker records conversion bindings while the tree stays immutable.
/// </summary>
public sealed class ValueProviderPatcher : IExprSemanticPatcher
{
    private ValueProviderPatcher()
    {
    }

    /// <summary>Gets the shared stateless patcher.</summary>
    public static ValueProviderPatcher Instance { get; } = new();

    /// <inheritdoc />
    public SyntaxNode Apply(SyntaxNode root, ExprSemanticModel model, ExprConfiguration configuration)
    {
        System.ArgumentNullException.ThrowIfNull(root);
        System.ArgumentNullException.ThrowIfNull(model);
        System.ArgumentNullException.ThrowIfNull(configuration);
        return root;
    }
}
