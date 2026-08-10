using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Expr.Builtins;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Execution;
using Expr.Optimization;
using Expr.Syntax;
using Expr.Types;

namespace Expr;

/// <summary>Provides the high-level parse, check, compile, and evaluation API for Expr.</summary>
public static class ExprEngine
{
    /// <summary>Parses expression source using syntax settings derived from a configuration.</summary>
    /// <param name="source">The Expr source text.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <returns>The parsed syntax tree.</returns>
    /// <exception cref="SyntaxException">The source is not valid Expr syntax.</exception>
    public static SyntaxTree Parse(string source, ExprConfiguration? configuration = null)
    {
        ExprConfiguration effectiveConfiguration = configuration ?? ExprConfiguration.Default;
        return new SyntaxParser().Parse(source, CreateParserOptions(effectiveConfiguration));
    }

    /// <summary>Attempts to parse expression source without throwing for a syntax error.</summary>
    /// <param name="source">The Expr source text.</param>
    /// <param name="tree">The parsed tree when successful.</param>
    /// <param name="diagnostic">The syntax diagnostic when unsuccessful.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <returns><see langword="true"/> when parsing succeeds.</returns>
    public static bool TryParse(
        string source,
        [NotNullWhen(true)] out SyntaxTree? tree,
        [NotNullWhen(false)] out SyntaxDiagnostic? diagnostic,
        ExprConfiguration? configuration = null)
    {
        ExprConfiguration effectiveConfiguration = configuration ?? ExprConfiguration.Default;
        return new SyntaxParser().TryParse(
            source,
            out tree,
            out diagnostic,
            CreateParserOptions(effectiveConfiguration));
    }

    /// <summary>Parses and statically checks expression source.</summary>
    /// <param name="source">The Expr source text.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <returns>The checked semantic model.</returns>
    /// <exception cref="SyntaxException">The source is not valid Expr syntax.</exception>
    /// <exception cref="ExprCheckException">The expression fails static checking.</exception>
    public static ExprSemanticModel Check(string source, ExprConfiguration? configuration = null)
    {
        ExprConfiguration effectiveConfiguration = configuration ?? ExprConfiguration.Default;
        return Check(Parse(source, effectiveConfiguration), effectiveConfiguration);
    }

    /// <summary>Statically checks a previously parsed syntax tree.</summary>
    /// <param name="tree">The parsed syntax tree.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <returns>The checked semantic model.</returns>
    /// <exception cref="ExprCheckException">The expression fails static checking.</exception>
    public static ExprSemanticModel Check(SyntaxTree tree, ExprConfiguration? configuration = null) =>
        new ExprChecker().Check(tree, configuration ?? ExprConfiguration.Default);

    /// <summary>Attempts to statically check a parsed tree without throwing for a checking diagnostic.</summary>
    /// <param name="tree">The parsed syntax tree.</param>
    /// <param name="model">The semantic model when successful.</param>
    /// <param name="diagnostic">The checking diagnostic when unsuccessful.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <returns><see langword="true"/> when checking succeeds.</returns>
    public static bool TryCheck(
        SyntaxTree tree,
        [NotNullWhen(true)] out ExprSemanticModel? model,
        [NotNullWhen(false)] out ExprCheckDiagnostic? diagnostic,
        ExprConfiguration? configuration = null) =>
        new ExprChecker().TryCheck(
            tree,
            out model,
            out diagnostic,
            configuration ?? ExprConfiguration.Default);

    /// <summary>Parses, checks, optimizes, and compiles expression source.</summary>
    /// <param name="source">The Expr source text.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <param name="compilationOptions">Optional bytecode-generation settings.</param>
    /// <returns>An immutable compilation that can be inspected and evaluated concurrently.</returns>
    public static CompiledExpression Compile(
        string source,
        ExprConfiguration? configuration = null,
        ExprCompilationOptions? compilationOptions = null)
    {
        ExprConfiguration effectiveConfiguration = configuration ?? ExprConfiguration.Default;
        return Compile(Parse(source, effectiveConfiguration), effectiveConfiguration, compilationOptions);
    }

    /// <summary>Checks, optimizes, and compiles a parsed or consumer-patched syntax tree.</summary>
    /// <param name="tree">The syntax tree to compile.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <param name="compilationOptions">Optional bytecode-generation settings.</param>
    /// <returns>An immutable compilation that can be inspected and evaluated concurrently.</returns>
    public static CompiledExpression Compile(
        SyntaxTree tree,
        ExprConfiguration? configuration = null,
        ExprCompilationOptions? compilationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ExprConfiguration effectiveConfiguration = configuration ?? ExprConfiguration.Default;
        ExprCompilationOptions effectiveCompilationOptions = compilationOptions ?? ExprCompilationOptions.Default;
        ExprSemanticModel model = Check(tree, effectiveConfiguration);
        model = ExprOptimizer.Optimize(model, effectiveConfiguration);
        ExprProgram program = ExprCompiler.Compile(model, effectiveConfiguration, effectiveCompilationOptions);
        return new CompiledExpression(model, program, effectiveConfiguration, effectiveCompilationOptions);
    }

    /// <summary>Evaluates a previously compiled expression.</summary>
    /// <param name="expression">The immutable compiled expression.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="options">Optional per-invocation resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The expression result.</returns>
    public static object? Run(
        CompiledExpression expression,
        object? environment = null,
        ExprEvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.Run(environment, options, cancellationToken);
    }

    /// <summary>Evaluates a previously compiled expression and returns resource and profiling measurements.</summary>
    /// <param name="expression">The immutable compiled expression.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="options">Optional per-invocation resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The expression result with evaluation measurements.</returns>
    public static ExprEvaluationResult RunDetailed(
        CompiledExpression expression,
        object? environment = null,
        ExprEvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.RunDetailed(environment, options, cancellationToken);
    }

    /// <summary>Parses, compiles, and evaluates expression source.</summary>
    /// <remarks>Compile once and call <see cref="Run"/> when evaluating the same expression repeatedly.</remarks>
    /// <param name="source">The Expr source text.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <param name="compilationOptions">Optional bytecode-generation settings.</param>
    /// <param name="evaluationOptions">Optional per-invocation resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The expression result.</returns>
    public static object? Evaluate(
        string source,
        object? environment = null,
        ExprConfiguration? configuration = null,
        ExprCompilationOptions? compilationOptions = null,
        ExprEvaluationOptions? evaluationOptions = null,
        CancellationToken cancellationToken = default) =>
        Compile(source, configuration, compilationOptions)
            .Run(environment, evaluationOptions, cancellationToken);

    /// <summary>Parses, compiles, and evaluates expression source with resource and profiling measurements.</summary>
    /// <remarks>Compile once and call <see cref="RunDetailed"/> when evaluating the same expression repeatedly.</remarks>
    /// <param name="source">The Expr source text.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="configuration">Optional immutable configuration.</param>
    /// <param name="compilationOptions">Optional bytecode-generation settings.</param>
    /// <param name="evaluationOptions">Optional per-invocation resource and profiling settings.</param>
    /// <param name="cancellationToken">Cancels instruction dispatch and host-call boundaries.</param>
    /// <returns>The expression result with evaluation measurements.</returns>
    public static ExprEvaluationResult EvaluateDetailed(
        string source,
        object? environment = null,
        ExprConfiguration? configuration = null,
        ExprCompilationOptions? compilationOptions = null,
        ExprEvaluationOptions? evaluationOptions = null,
        CancellationToken cancellationToken = default) =>
        Compile(source, configuration, compilationOptions)
            .RunDetailed(environment, evaluationOptions, cancellationToken);

    private static SyntaxParserOptions CreateParserOptions(ExprConfiguration configuration)
    {
        var disabledBuiltins = new HashSet<string>(configuration.DisabledBuiltins, StringComparer.Ordinal);
        foreach (string standardBuiltin in ExprBuiltinLibrary.Standard.Functions.Select(static function => function.Name))
        {
            if (!configuration.Builtins.ContainsKey(standardBuiltin))
            {
                _ = disabledBuiltins.Add(standardBuiltin);
            }
        }

        var overriddenBuiltins = new HashSet<string>(configuration.Functions.Keys, StringComparer.Ordinal);
        overriddenBuiltins.UnionWith(disabledBuiltins);
        if (configuration.Environment is not null)
        {
            overriddenBuiltins.UnionWith(configuration.Environment.Members
                .Where(static pair => pair.Value.Type.Kind is ExprTypeKind.Function)
                .Select(static pair => pair.Key));
        }

        return new SyntaxParserOptions
        {
            MaximumNodeCount = configuration.MaximumNodeCount,
            MaximumParseDepth = Math.Min(
                new SyntaxParserOptions().MaximumParseDepth,
                configuration.MaximumCheckDepth),
            DisableIfOperator = configuration.DisableIfOperator,
            DisabledBuiltins = disabledBuiltins,
            OverriddenBuiltins = overriddenBuiltins,
        };
    }
}
