using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Expr.Syntax;

/// <summary>Base type for every public Expr abstract syntax tree node.</summary>
/// <param name="Location">The source location that introduced the node.</param>
public abstract record SyntaxNode(SourceLocation Location);

/// <summary>Represents the Expr <c>nil</c> literal.</summary>
/// <param name="Location">The source location.</param>
public sealed record NilNode(SourceLocation Location) : SyntaxNode(Location)
{
    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents an identifier.</summary>
/// <param name="Name">The identifier name.</param>
/// <param name="Location">The source location.</param>
public sealed record IdentifierNode(string Name, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the identifier name.</summary>
    public string Name
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            field = value;
        }
    } = !string.IsNullOrEmpty(Name)
        ? Name
        : throw new ArgumentException("An identifier name cannot be empty.", nameof(Name));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents an integer literal.</summary>
/// <param name="Value">The literal value.</param>
/// <param name="Location">The source location.</param>
public sealed record IntegerNode(long Value, SourceLocation Location) : SyntaxNode(Location)
{
    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a floating-point literal.</summary>
/// <param name="Value">The literal value.</param>
/// <param name="Location">The source location.</param>
public sealed record FloatNode(double Value, SourceLocation Location) : SyntaxNode(Location)
{
    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a Boolean literal.</summary>
/// <param name="Value">The literal value.</param>
/// <param name="Location">The source location.</param>
public sealed record BooleanNode(bool Value, SourceLocation Location) : SyntaxNode(Location)
{
    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a string literal.</summary>
/// <param name="Value">The decoded string value.</param>
/// <param name="Location">The source location.</param>
public sealed record StringNode(string Value, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the decoded string value.</summary>
    public string Value
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Value ?? throw new ArgumentNullException(nameof(Value));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a byte-string literal.</summary>
public sealed record BytesNode : SyntaxNode
{
    private readonly byte[] value;

    /// <summary>Initializes a byte-string node.</summary>
    /// <param name="value">The decoded bytes.</param>
    /// <param name="location">The source location.</param>
    public BytesNode(ReadOnlySpan<byte> value, SourceLocation location)
        : base(location)
    {
        this.value = value.ToArray();
    }

    /// <summary>Gets a defensive copy of the decoded bytes.</summary>
    public ReadOnlyMemory<byte> Value => value.ToArray();

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a constant introduced by an optimizer or AST consumer.</summary>
/// <param name="Value">The constant value.</param>
/// <param name="Location">The source location.</param>
public sealed record ConstantNode(object? Value, SourceLocation Location) : SyntaxNode(Location)
{
    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a unary operation.</summary>
/// <param name="Operator">The operator spelling.</param>
/// <param name="Operand">The operand.</param>
/// <param name="Location">The operator source location.</param>
public sealed record UnaryNode(string Operator, SyntaxNode Operand, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the operator spelling.</summary>
    public string Operator
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            field = value;
        }
    } = !string.IsNullOrEmpty(Operator)
        ? Operator
        : throw new ArgumentException("An operator cannot be empty.", nameof(Operator));

    /// <summary>Gets the operand.</summary>
    public SyntaxNode Operand
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Operand ?? throw new ArgumentNullException(nameof(Operand));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a binary operation.</summary>
/// <param name="Operator">The operator spelling.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
/// <param name="Location">The operator source location.</param>
public sealed record BinaryNode(string Operator, SyntaxNode Left, SyntaxNode Right, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the operator spelling.</summary>
    public string Operator
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            field = value;
        }
    } = !string.IsNullOrEmpty(Operator)
        ? Operator
        : throw new ArgumentException("An operator cannot be empty.", nameof(Operator));

    /// <summary>Gets the left operand.</summary>
    public SyntaxNode Left
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Left ?? throw new ArgumentNullException(nameof(Left));

    /// <summary>Gets the right operand.</summary>
    public SyntaxNode Right
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Right ?? throw new ArgumentNullException(nameof(Right));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Groups one or more optional member accesses.</summary>
/// <param name="Expression">The grouped expression.</param>
/// <param name="Location">The source location.</param>
public sealed record ChainNode(SyntaxNode Expression, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the grouped expression.</summary>
    public SyntaxNode Expression
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Expression ?? throw new ArgumentNullException(nameof(Expression));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents property or index access.</summary>
/// <param name="Target">The value being accessed.</param>
/// <param name="Property">The property name or index expression.</param>
/// <param name="Optional">Whether the access is optional.</param>
/// <param name="IsMethod">Whether the member is used as a method callee.</param>
/// <param name="Location">The property or bracket source location.</param>
public sealed record MemberNode(
    SyntaxNode Target,
    SyntaxNode Property,
    bool Optional,
    bool IsMethod,
    SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the value being accessed.</summary>
    public SyntaxNode Target
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Target ?? throw new ArgumentNullException(nameof(Target));

    /// <summary>Gets the property name or index expression.</summary>
    public SyntaxNode Property
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Property ?? throw new ArgumentNullException(nameof(Property));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents array or string slicing.</summary>
/// <param name="Target">The value being sliced.</param>
/// <param name="From">The optional inclusive lower bound.</param>
/// <param name="To">The optional exclusive upper bound.</param>
/// <param name="Location">The opening bracket location.</param>
public sealed record SliceNode(
    SyntaxNode Target,
    SyntaxNode? From,
    SyntaxNode? To,
    SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the value being sliced.</summary>
    public SyntaxNode Target
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Target ?? throw new ArgumentNullException(nameof(Target));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents an ordinary function or method call.</summary>
public sealed record CallNode : SyntaxNode
{
    /// <summary>Initializes a call node.</summary>
    /// <param name="callee">The called expression.</param>
    /// <param name="arguments">The call arguments.</param>
    /// <param name="location">The source location.</param>
    public CallNode(SyntaxNode callee, IEnumerable<SyntaxNode> arguments, SourceLocation location)
        : base(location)
    {
        ArgumentNullException.ThrowIfNull(callee);
        Callee = callee;
        Arguments = SyntaxCollections.Copy(arguments);
    }

    /// <summary>Gets the called expression.</summary>
    public SyntaxNode Callee { get; }

    /// <summary>Gets the call arguments.</summary>
    public IReadOnlyList<SyntaxNode> Arguments { get; }

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a call to an Expr built-in.</summary>
public sealed record BuiltinNode : SyntaxNode
{
    /// <summary>Initializes a built-in call node.</summary>
    /// <param name="name">The built-in name.</param>
    /// <param name="arguments">The call arguments.</param>
    /// <param name="location">The source location.</param>
    public BuiltinNode(
        string name,
        IEnumerable<SyntaxNode> arguments,
        SourceLocation location)
        : this(name, arguments, location, false, null, null)
    {
    }

    /// <summary>Initializes a built-in call node with optimizer metadata.</summary>
    public BuiltinNode(
        string name,
        IEnumerable<SyntaxNode> arguments,
        SourceLocation location,
        bool throws = false,
        SyntaxNode? map = null,
        int? threshold = null)
        : base(location)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (threshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "An optimizer threshold must be positive.");
        }

        if (throws && name is not ("find" or "findLast"))
        {
            throw new ArgumentException("Only find built-ins can carry throwing lookup metadata.", nameof(throws));
        }

        if (map is not null && name is not ("filter" or "find" or "findLast"))
        {
            throw new ArgumentException("Only filter and find built-ins can carry a mapped result.", nameof(map));
        }

        if (threshold is not null && name is not "count")
        {
            throw new ArgumentException("Only count can carry an optimizer threshold.", nameof(threshold));
        }

        Name = name;
        Arguments = SyntaxCollections.Copy(arguments);
        Throws = throws;
        Map = map;
        Threshold = threshold;
    }

    /// <summary>Gets the built-in name.</summary>
    public string Name { get; }

    /// <summary>Gets the arguments.</summary>
    public IReadOnlyList<SyntaxNode> Arguments { get; }

    /// <summary>Gets whether optimizer-generated access can throw.</summary>
    public bool Throws { get; }

    /// <summary>Gets an optional optimizer-generated map expression.</summary>
    public SyntaxNode? Map { get; }

    /// <summary>Gets an optional optimizer-generated count threshold.</summary>
    public int? Threshold { get; }

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Wraps a predicate argument.</summary>
/// <param name="Body">The predicate body.</param>
/// <param name="Location">The predicate source location.</param>
public sealed record PredicateNode(SyntaxNode Body, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the predicate body.</summary>
    public SyntaxNode Body
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Body ?? throw new ArgumentNullException(nameof(Body));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents the current predicate value or a named predicate pointer.</summary>
/// <param name="Name">The pointer name, or an empty string for the current value.</param>
/// <param name="Location">The pointer source location.</param>
public sealed record PointerNode(string Name, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the pointer name.</summary>
    public string Name
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Name ?? throw new ArgumentNullException(nameof(Name));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a conditional expression.</summary>
/// <param name="Condition">The condition.</param>
/// <param name="WhenTrue">The expression evaluated when true.</param>
/// <param name="WhenFalse">The expression evaluated when false.</param>
/// <param name="IsTernary">Whether this used ternary rather than if/else syntax.</param>
/// <param name="Location">The conditional source location.</param>
public sealed record ConditionalNode(
    SyntaxNode Condition,
    SyntaxNode WhenTrue,
    SyntaxNode WhenFalse,
    bool IsTernary,
    SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the condition.</summary>
    public SyntaxNode Condition
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Condition ?? throw new ArgumentNullException(nameof(Condition));

    /// <summary>Gets the expression evaluated when true.</summary>
    public SyntaxNode WhenTrue
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = WhenTrue ?? throw new ArgumentNullException(nameof(WhenTrue));

    /// <summary>Gets the expression evaluated when false.</summary>
    public SyntaxNode WhenFalse
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = WhenFalse ?? throw new ArgumentNullException(nameof(WhenFalse));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents an Expr <c>let</c> binding and its body.</summary>
/// <param name="Name">The variable name.</param>
/// <param name="Value">The bound value.</param>
/// <param name="Body">The expression in which the variable is bound.</param>
/// <param name="Location">The variable name source location.</param>
public sealed record VariableDeclaratorNode(
    string Name,
    SyntaxNode Value,
    SyntaxNode Body,
    SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the variable name.</summary>
    public string Name
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            field = value;
        }
    } = !string.IsNullOrEmpty(Name)
        ? Name
        : throw new ArgumentException("A variable name cannot be empty.", nameof(Name));

    /// <summary>Gets the bound value.</summary>
    public SyntaxNode Value
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Value ?? throw new ArgumentNullException(nameof(Value));

    /// <summary>Gets the expression in which the variable is bound.</summary>
    public SyntaxNode Body
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Body ?? throw new ArgumentNullException(nameof(Body));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents semicolon-separated expressions.</summary>
public sealed record SequenceNode : SyntaxNode
{
    /// <summary>Initializes a sequence node.</summary>
    /// <param name="expressions">The sequence expressions.</param>
    /// <param name="location">The source location.</param>
    public SequenceNode(IEnumerable<SyntaxNode> expressions, SourceLocation location)
        : base(location)
    {
        Expressions = SyntaxCollections.Copy(expressions);
        if (Expressions.Count == 0)
        {
            throw new ArgumentException("A sequence must contain at least one expression.", nameof(expressions));
        }
    }

    /// <summary>Gets the sequence expressions.</summary>
    public IReadOnlyList<SyntaxNode> Expressions { get; }

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents an array literal.</summary>
public sealed record ArrayNode : SyntaxNode
{
    /// <summary>Initializes an array node.</summary>
    /// <param name="elements">The array elements.</param>
    /// <param name="location">The source location.</param>
    public ArrayNode(IEnumerable<SyntaxNode> elements, SourceLocation location)
        : base(location) => Elements = SyntaxCollections.Copy(elements);

    /// <summary>Gets the array elements.</summary>
    public IReadOnlyList<SyntaxNode> Elements { get; }

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents a map literal.</summary>
public sealed record MapNode : SyntaxNode
{
    /// <summary>Initializes a map node.</summary>
    /// <param name="pairs">The map pairs.</param>
    /// <param name="location">The source location.</param>
    public MapNode(IEnumerable<PairNode> pairs, SourceLocation location)
        : base(location) => Pairs = SyntaxCollections.Copy(pairs);

    /// <summary>Gets the map pairs.</summary>
    public IReadOnlyList<PairNode> Pairs { get; }

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

/// <summary>Represents one key-value pair in a map literal.</summary>
/// <param name="Key">The key expression.</param>
/// <param name="Value">The value expression.</param>
/// <param name="Location">The map opening-brace source location.</param>
public sealed record PairNode(SyntaxNode Key, SyntaxNode Value, SourceLocation Location) : SyntaxNode(Location)
{
    /// <summary>Gets the key expression.</summary>
    public SyntaxNode Key
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Key ?? throw new ArgumentNullException(nameof(Key));

    /// <summary>Gets the value expression.</summary>
    public SyntaxNode Value
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Value ?? throw new ArgumentNullException(nameof(Value));

    /// <inheritdoc />
    public override string ToString() => SyntaxPrinter.Print(this);
}

internal static class SyntaxCollections
{
    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (Array.IndexOf(copy, null) >= 0)
        {
            throw new ArgumentException("Syntax collections cannot contain null elements.", nameof(values));
        }

        return new ReadOnlyCollection<T>(copy);
    }
}
