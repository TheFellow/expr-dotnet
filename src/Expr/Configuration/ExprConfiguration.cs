using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Expr.Builtins;
using Expr.Patching;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Configuration;

/// <summary>Contains immutable settings used while parsing, checking, optimizing, and evaluating an expression.</summary>
public sealed class ExprConfiguration
{
    /// <summary>Gets the default VM memory budget.</summary>
    public const ulong DefaultMemoryBudget = 1_000_000;

    /// <summary>Gets the default maximum number of syntax nodes.</summary>
    public const int DefaultMaximumNodeCount = 10_000;

    private ExprConfiguration(
        ExprEnvironmentSchema? environment,
        bool strict,
        ExprTypeDescriptor? expectedType,
        bool allowAnyResult,
        bool optimize,
        bool shortCircuit,
        bool disableIfOperator,
        int maximumSourceLength,
        int maximumNodeCount,
        int maximumCheckDepth,
        ulong memoryBudget,
        TimeSpan regularExpressionTimeout,
        int maximumRegularExpressionLength,
        IReadOnlyDictionary<string, ExprFunction> functions,
        IReadOnlyDictionary<string, ExprFunction> builtins,
        IReadOnlySet<string> disabledBuiltins,
        IReadOnlySet<string> constantFunctions,
        IReadOnlyList<IExprSemanticPatcher> patchers)
    {
        Environment = environment;
        Strict = strict;
        ExpectedType = expectedType;
        AllowAnyResult = allowAnyResult;
        Optimize = optimize;
        ShortCircuit = shortCircuit;
        DisableIfOperator = disableIfOperator;
        MaximumSourceLength = maximumSourceLength;
        MaximumNodeCount = maximumNodeCount;
        MaximumCheckDepth = maximumCheckDepth;
        MemoryBudget = memoryBudget;
        RegularExpressionTimeout = regularExpressionTimeout;
        MaximumRegularExpressionLength = maximumRegularExpressionLength;
        Functions = functions;
        Builtins = builtins;
        DisabledBuiltins = disabledBuiltins;
        ConstantFunctions = constantFunctions;
        Patchers = patchers;
    }

    /// <summary>Gets the default configuration.</summary>
    public static ExprConfiguration Default { get; } = new(
        null,
        true,
        null,
        true,
        true,
        true,
        false,
        SyntaxParserOptions.DefaultMaximumSourceLength,
        DefaultMaximumNodeCount,
        1_024,
        DefaultMemoryBudget,
        TimeSpan.FromMilliseconds(250),
        16_384,
        EmptyFunctions(),
        SnapshotFunctions(ExprBuiltinLibrary.Standard.Functions),
        EmptyNames(),
        EmptyNames(),
        Array.Empty<IExprSemanticPatcher>());

    /// <summary>Gets the optional host-environment schema.</summary>
    public ExprEnvironmentSchema? Environment { get; }

    /// <summary>Gets whether unknown top-level names are rejected.</summary>
    public bool Strict { get; }

    /// <summary>Gets the required result type, or <see langword="null"/> when unconstrained.</summary>
    public ExprTypeDescriptor? ExpectedType { get; }

    /// <summary>Gets whether an <c>any</c> result satisfies an expected result contract.</summary>
    public bool AllowAnyResult { get; }

    /// <summary>Gets whether optimizer passes are enabled.</summary>
    public bool Optimize { get; }

    /// <summary>Gets whether logical operators use short-circuit evaluation.</summary>
    public bool ShortCircuit { get; }

    /// <summary>Gets whether <c>if</c> and <c>else</c> are parsed as ordinary identifiers.</summary>
    public bool DisableIfOperator { get; }

    /// <summary>Gets the maximum syntax node count, or zero for no limit.</summary>
    public int MaximumNodeCount { get; }

    /// <summary>Gets the maximum source length in UTF-16 code units, or zero for no limit.</summary>
    public int MaximumSourceLength { get; }

    /// <summary>Gets the maximum checker traversal depth.</summary>
    public int MaximumCheckDepth { get; }

    /// <summary>Gets the VM memory budget.</summary>
    public ulong MemoryBudget { get; }

    /// <summary>Gets the timeout applied to dynamic regular-expression evaluation.</summary>
    public TimeSpan RegularExpressionTimeout { get; }

    /// <summary>Gets the maximum accepted regular-expression pattern length.</summary>
    public int MaximumRegularExpressionLength { get; }

    /// <summary>Gets host-defined functions by ordinal name.</summary>
    public IReadOnlyDictionary<string, ExprFunction> Functions { get; }

    /// <summary>Gets built-in functions by ordinal name.</summary>
    public IReadOnlyDictionary<string, ExprFunction> Builtins { get; }

    /// <summary>Gets names of disabled built-ins.</summary>
    public IReadOnlySet<string> DisabledBuiltins { get; }

    /// <summary>Gets functions eligible for compile-time constant folding.</summary>
    public IReadOnlySet<string> ConstantFunctions { get; }

    /// <summary>Gets semantic tree patchers in registration order.</summary>
    public IReadOnlyList<IExprSemanticPatcher> Patchers { get; }

    /// <summary>Returns a configuration using the supplied environment schema.</summary>
    /// <param name="environment">The environment schema.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithEnvironment(ExprEnvironmentSchema environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Copy(environment: environment, strict: environment.IsStrict);
    }

    /// <summary>Returns a configuration that accepts unknown variables.</summary>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration AllowUndefinedVariables() => Copy(strict: false);

    /// <summary>Returns a configuration requiring the supplied result type.</summary>
    /// <param name="type">The expected result type.</param>
    /// <param name="warnOnAny">Whether an inferred <c>any</c> result is rejected.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithExpectedType(ExprTypeDescriptor type, bool warnOnAny = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Copy(expectedType: new OptionalType(type), allowAnyResult: !warnOnAny);
    }

    /// <summary>Returns a configuration with or without optimizer passes.</summary>
    /// <param name="enabled">Whether optimization is enabled.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithOptimization(bool enabled) => Copy(optimize: enabled);

    /// <summary>Returns a configuration with or without short-circuit evaluation.</summary>
    /// <param name="enabled">Whether short-circuiting is enabled.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithShortCircuit(bool enabled) => Copy(shortCircuit: enabled);

    /// <summary>Returns a configuration that treats <c>if</c> as syntax or as a function name.</summary>
    /// <param name="disabled">Whether conditional syntax is disabled.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithIfOperatorDisabled(bool disabled = true) => Copy(disableIfOperator: disabled);

    /// <summary>Returns a configuration with the requested syntax-node budget.</summary>
    /// <param name="maximum">The maximum, or zero for no limit.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithMaximumNodeCount(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        return Copy(maximumNodeCount: maximum);
    }

    /// <summary>Returns a configuration with the requested source-length budget.</summary>
    /// <param name="maximum">The maximum UTF-16 code units, or zero for no limit.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithMaximumSourceLength(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        return Copy(maximumSourceLength: maximum);
    }

    /// <summary>Returns a configuration with the requested checker depth limit.</summary>
    /// <param name="maximum">The positive maximum depth.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithMaximumCheckDepth(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        return Copy(maximumCheckDepth: maximum);
    }

    /// <summary>Returns a configuration with the requested evaluation memory budget.</summary>
    /// <param name="budget">The budget, or zero for no limit.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithMemoryBudget(ulong budget) => Copy(memoryBudget: budget);

    /// <summary>Returns a configuration with explicit regular-expression safety controls.</summary>
    /// <param name="timeout">The positive match timeout.</param>
    /// <param name="maximumPatternLength">The positive maximum pattern length.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithRegularExpressionLimits(TimeSpan timeout, int maximumPatternLength)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPatternLength, 1);
        return Copy(regularExpressionTimeout: timeout, maximumRegularExpressionLength: maximumPatternLength);
    }

    /// <summary>Registers or replaces a host function.</summary>
    /// <param name="function">The function declaration.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithFunction(ExprFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Copy(functions: Add(Functions, function));
    }

    /// <summary>Registers or replaces a built-in function declaration.</summary>
    /// <param name="function">The built-in declaration.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithBuiltin(ExprFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Copy(builtins: Add(Builtins, function));
    }

    /// <summary>Replaces the complete built-in function table.</summary>
    /// <param name="functions">The built-ins to install.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithBuiltins(IEnumerable<ExprFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        return Copy(builtins: SnapshotFunctions(functions));
    }

    /// <summary>Disables one built-in.</summary>
    /// <param name="name">The built-in name.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration DisableBuiltin(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Copy(disabledBuiltins: Add(DisabledBuiltins, name));
    }

    /// <summary>Disables every currently registered built-in.</summary>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration DisableAllBuiltins() => Copy(
        disabledBuiltins: SnapshotNames(Builtins.Keys));

    /// <summary>Re-enables one built-in.</summary>
    /// <param name="name">The built-in name.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration EnableBuiltin(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Copy(disabledBuiltins: Remove(DisabledBuiltins, name));
    }

    /// <summary>Marks a function as eligible for compile-time constant folding.</summary>
    /// <param name="name">The function name.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithConstantFunction(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Functions.ContainsKey(name))
        {
            throw new ArgumentException(
                $"Constant expression function {name} must be registered with WithFunction.",
                nameof(name));
        }

        return Copy(constantFunctions: Add(ConstantFunctions, name));
    }

    /// <summary>Adds a semantic patcher.</summary>
    /// <param name="patcher">The patcher.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithPatcher(IExprSemanticPatcher patcher)
    {
        ArgumentNullException.ThrowIfNull(patcher);
        return Copy(patchers: Array.AsReadOnly(Patchers.Append(patcher).ToArray()));
    }

    /// <summary>Registers an operator overload patcher.</summary>
    /// <param name="operatorName">The binary operator spelling.</param>
    /// <param name="functionNames">Candidate functions in priority order.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithOperator(string operatorName, params string[] functionNames) =>
        WithPatcher(new OperatorOverridePatcher(operatorName, functionNames));

    /// <summary>Injects a host cancellation token into compatible calls.</summary>
    /// <remarks>The idiomatic final token parameter is preferred; a leading token remains supported for port parity.</remarks>
    /// <param name="environmentName">The environment variable containing a <see cref="System.Threading.CancellationToken"/>.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithContext(string environmentName) =>
        WithPatcher(new ContextArgumentPatcher(environmentName));

    /// <summary>Enables compile-time-aware conversion of host values implementing value-provider contracts.</summary>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithValueProviders() => WithPatcher(ValueProviderPatcher.Instance);

    /// <summary>Supplies a default time zone to <c>date</c> and <c>now</c>.</summary>
    /// <param name="timeZone">The default time zone.</param>
    /// <returns>A new configuration.</returns>
    public ExprConfiguration WithTimeZone(TimeZoneInfo timeZone) =>
        WithPatcher(new TimeZonePatcher(timeZone));

    /// <summary>Supplies a named default time zone to <c>date</c> and <c>now</c>.</summary>
    /// <param name="timeZoneId">An IANA or Windows time-zone identifier recognized by the host.</param>
    /// <returns>A new configuration.</returns>
    /// <exception cref="TimeZoneNotFoundException">The identifier is not installed on the host.</exception>
    /// <exception cref="InvalidTimeZoneException">The installed time-zone data is invalid.</exception>
    public ExprConfiguration WithTimeZone(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        return WithTimeZone(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }

    internal bool TryGetFunction(string name, out ExprFunction? function)
    {
        if (Functions.TryGetValue(name, out function))
        {
            return true;
        }

        return !DisabledBuiltins.Contains(name) && Builtins.TryGetValue(name, out function);
    }

    private ExprConfiguration Copy(
        ExprEnvironmentSchema? environment = null,
        bool? strict = null,
        OptionalType? expectedType = null,
        bool? allowAnyResult = null,
        bool? optimize = null,
        bool? shortCircuit = null,
        bool? disableIfOperator = null,
        int? maximumSourceLength = null,
        int? maximumNodeCount = null,
        int? maximumCheckDepth = null,
        ulong? memoryBudget = null,
        TimeSpan? regularExpressionTimeout = null,
        int? maximumRegularExpressionLength = null,
        IReadOnlyDictionary<string, ExprFunction>? functions = null,
        IReadOnlyDictionary<string, ExprFunction>? builtins = null,
        IReadOnlySet<string>? disabledBuiltins = null,
        IReadOnlySet<string>? constantFunctions = null,
        IReadOnlyList<IExprSemanticPatcher>? patchers = null) => new(
            environment ?? Environment,
            strict ?? Strict,
            expectedType?.Value ?? ExpectedType,
            allowAnyResult ?? AllowAnyResult,
            optimize ?? Optimize,
            shortCircuit ?? ShortCircuit,
            disableIfOperator ?? DisableIfOperator,
            maximumSourceLength ?? MaximumSourceLength,
            maximumNodeCount ?? MaximumNodeCount,
            maximumCheckDepth ?? MaximumCheckDepth,
            memoryBudget ?? MemoryBudget,
            regularExpressionTimeout ?? RegularExpressionTimeout,
            maximumRegularExpressionLength ?? MaximumRegularExpressionLength,
            functions ?? Functions,
            builtins ?? Builtins,
            disabledBuiltins ?? DisabledBuiltins,
            constantFunctions ?? ConstantFunctions,
            patchers ?? Patchers);

    private static IReadOnlyDictionary<string, ExprFunction> Add(
        IReadOnlyDictionary<string, ExprFunction> source,
        ExprFunction function)
    {
        var copy = new Dictionary<string, ExprFunction>(source, StringComparer.Ordinal)
        {
            [function.Name] = function,
        };
        return new ReadOnlyDictionary<string, ExprFunction>(copy);
    }

    private static IReadOnlySet<string> Add(IReadOnlySet<string> source, string name)
    {
        var copy = new HashSet<string>(source, StringComparer.Ordinal) { name };
        return new ReadOnlySet<string>(copy);
    }

    private static IReadOnlySet<string> Remove(IReadOnlySet<string> source, string name)
    {
        var copy = new HashSet<string>(source, StringComparer.Ordinal);
        _ = copy.Remove(name);
        return new ReadOnlySet<string>(copy);
    }

    private static IReadOnlyDictionary<string, ExprFunction> EmptyFunctions() =>
        new ReadOnlyDictionary<string, ExprFunction>(new Dictionary<string, ExprFunction>(StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, ExprFunction> SnapshotFunctions(IEnumerable<ExprFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        return new ReadOnlyDictionary<string, ExprFunction>(
            functions.ToDictionary(static function => function.Name, StringComparer.Ordinal));
    }

    private static IReadOnlySet<string> EmptyNames() =>
        new ReadOnlySet<string>(new HashSet<string>(StringComparer.Ordinal));

    private static IReadOnlySet<string> SnapshotNames(IEnumerable<string> names) =>
        new ReadOnlySet<string>(new HashSet<string>(names, StringComparer.Ordinal));

    private sealed record OptionalType(ExprTypeDescriptor Value);
}
