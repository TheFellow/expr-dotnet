namespace Expr.Compilation;

/// <summary>Configures bytecode generation independently of expression semantics.</summary>
public sealed record ExprCompilationOptions
{
    /// <summary>Gets the default compilation options.</summary>
    public static ExprCompilationOptions Default { get; } = new();

    /// <summary>Gets or initializes whether every syntax node emits profiling boundaries.</summary>
    public bool EnableProfiling { get; init; }
}
