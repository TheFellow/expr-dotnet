using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Expr.Checking;
using Expr.Runtime;
using Expr.Syntax;

namespace Expr.Compilation;

/// <summary>Describes a member selected by static checking.</summary>
public sealed class ExprMemberOperand
{
    private readonly IReadOnlyList<string> path;

    /// <summary>Initializes a statically bound member operand.</summary>
    /// <param name="name">The expression-visible name.</param>
    /// <param name="kind">The binding category.</param>
    /// <param name="member">The cached CLR member, when applicable.</param>
    /// <param name="environmentMember">The reflection-free environment accessor, when applicable.</param>
    /// <param name="path">The expression-visible access path.</param>
    public ExprMemberOperand(
        string name,
        ExprMemberBindingKind kind,
        MemberInfo? member = null,
        ExprEnvironmentMember? environmentMember = null,
        IEnumerable<string>? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        Member = member;
        EnvironmentMember = environmentMember;
        this.path = new ReadOnlyCollection<string>((path ?? [name]).ToArray());
    }

    /// <summary>Gets the expression-visible member name.</summary>
    public string Name { get; }

    /// <summary>Gets the static binding category.</summary>
    public ExprMemberBindingKind Kind { get; }

    /// <summary>Gets the cached CLR member, when applicable.</summary>
    public MemberInfo? Member { get; }

    /// <summary>Gets the reflection-free environment member and accessor, when applicable.</summary>
    public ExprEnvironmentMember? EnvironmentMember { get; }

    /// <summary>Gets the member path from the access root.</summary>
    public IReadOnlyList<string> Path => path;

    /// <inheritdoc />
    public override string ToString() => string.Join('.', path);
}

/// <summary>Contains a constant regular expression compiled with explicit safety limits.</summary>
public sealed class ExprRegularExpressionOperand
{
    /// <summary>Initializes and validates a constant regular expression.</summary>
    /// <param name="pattern">The expression pattern.</param>
    /// <param name="timeout">The match timeout.</param>
    public ExprRegularExpressionOperand(string pattern, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Pattern = pattern;
        Timeout = timeout;
        CompiledExpression = new Regex(
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            timeout);
    }

    /// <summary>Gets the original pattern.</summary>
    public string Pattern { get; }

    /// <summary>Gets the configured match timeout.</summary>
    public TimeSpan Timeout { get; }

    internal Regex CompiledExpression { get; }

    /// <inheritdoc />
    public override string ToString() => Pattern;
}

/// <summary>Represents an error produced by predicate bytecode.</summary>
/// <param name="Message">The stable error message.</param>
public sealed record ExprErrorOperand(string Message)
{
    /// <inheritdoc />
    public override string ToString() => Message;
}

/// <summary>Identifies one syntax node represented in profiling bytecode.</summary>
/// <param name="Id">The stable point identifier within a program.</param>
/// <param name="ParentId">The containing point identifier, or <see langword="null"/> for the root.</param>
/// <param name="NodeKind">The syntax-node type name.</param>
/// <param name="Location">The profiled source range.</param>
public sealed record ExprProfilePoint(
    int Id,
    int? ParentId,
    string NodeKind,
    SourceLocation Location);
