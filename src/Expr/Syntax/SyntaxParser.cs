using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Expr.Syntax;

/// <summary>Configures syntax parsing independently of checking and evaluation.</summary>
public sealed record SyntaxParserOptions
{
    /// <summary>Gets the default maximum number of AST nodes.</summary>
    public const int DefaultMaximumNodeCount = 10_000;

    /// <summary>Gets the default maximum source length in UTF-16 code units.</summary>
    public const int DefaultMaximumSourceLength = 1_000_000;

    /// <summary>Gets or initializes the maximum source length, or zero for no limit.</summary>
    public int MaximumSourceLength { get; init; } = DefaultMaximumSourceLength;

    /// <summary>Gets or initializes the maximum number of AST nodes, or zero for no limit.</summary>
    public int MaximumNodeCount { get; init; } = DefaultMaximumNodeCount;

    /// <summary>Gets or initializes the maximum recursive grammar depth.</summary>
    public int MaximumParseDepth { get; init; } = 512;

    /// <summary>Gets or initializes whether <c>if</c> and <c>else</c> are ordinary identifiers.</summary>
    public bool DisableIfOperator { get; init; }

    /// <summary>Gets or initializes built-ins disabled by the host configuration.</summary>
    public IReadOnlySet<string> DisabledBuiltins { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets or initializes names whose host definitions override built-ins.</summary>
    public IReadOnlySet<string> OverriddenBuiltins { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>Contains an immutable syntax tree and its original source.</summary>
/// <param name="Root">The root syntax node.</param>
/// <param name="Source">The original source.</param>
public sealed record SyntaxTree(SyntaxNode Root, SourceText Source);

/// <summary>Parses Expr tokens with the same Pratt grammar and precedences as expr-lang/expr.</summary>
public sealed class SyntaxParser
{
    private static readonly IReadOnlyDictionary<string, OperatorInfo> BinaryOperators =
        new Dictionary<string, OperatorInfo>(StringComparer.Ordinal)
        {
            ["|"] = new(0, false),
            ["or"] = new(10, false),
            ["||"] = new(10, false),
            ["and"] = new(15, false),
            ["&&"] = new(15, false),
            ["=="] = new(20, false),
            ["!="] = new(20, false),
            ["<"] = new(20, false),
            [">"] = new(20, false),
            [">="] = new(20, false),
            ["<="] = new(20, false),
            ["in"] = new(20, false),
            ["matches"] = new(20, false),
            ["contains"] = new(20, false),
            ["startsWith"] = new(20, false),
            ["endsWith"] = new(20, false),
            [".."] = new(25, false),
            ["+"] = new(30, false),
            ["-"] = new(30, false),
            ["*"] = new(60, false),
            ["/"] = new(60, false),
            ["%"] = new(60, false),
            ["**"] = new(100, true),
            ["^"] = new(100, true),
            ["??"] = new(500, false),
        };

    private static readonly IReadOnlyDictionary<string, int> UnaryOperators =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["not"] = 50,
            ["!"] = 50,
            ["-"] = 90,
            ["+"] = 90,
        };

    private static readonly IReadOnlyDictionary<string, PredicateSignature> Predicates =
        new Dictionary<string, PredicateSignature>(StringComparer.Ordinal)
        {
            ["all"] = new(false, false),
            ["none"] = new(false, false),
            ["any"] = new(false, false),
            ["one"] = new(false, false),
            ["filter"] = new(false, false),
            ["map"] = new(false, false),
            ["find"] = new(false, false),
            ["findIndex"] = new(false, false),
            ["findLast"] = new(false, false),
            ["findLastIndex"] = new(false, false),
            ["groupBy"] = new(false, false),
            ["count"] = new(true, false),
            ["sum"] = new(true, false),
            ["sortBy"] = new(false, true),
            ["reduce"] = new(false, true),
        };

    private static readonly IReadOnlySet<string> Builtins = new HashSet<string>(StringComparer.Ordinal)
    {
        "all", "none", "any", "one", "filter", "map", "find", "findIndex", "findLast",
        "findLastIndex", "count", "sum", "groupBy", "sortBy", "reduce", "len", "type", "abs",
        "ceil", "floor", "round", "int", "float", "string", "trim", "trimPrefix", "trimSuffix",
        "upper", "lower", "split", "splitAfter", "replace", "repeat", "join", "indexOf",
        "lastIndexOf", "hasPrefix", "hasSuffix", "max", "min", "mean", "median", "toJSON",
        "fromJSON", "toBase64", "fromBase64", "now", "duration", "date", "timezone", "first",
        "last", "get", "take", "keys", "values", "toPairs", "fromPairs", "reverse", "uniq",
        "concat", "flatten", "sort", "bitand", "bitor", "bitxor", "bitnand", "bitshl",
        "bitshr", "bitushr", "bitnot",
    };

    private IReadOnlyList<SyntaxToken> tokens = [];
    private SyntaxParserOptions options = new();
    private SourceText source = new(string.Empty);
    private int index;
    private int predicateDepth;
    private int nodeCount;
    private int parseDepth;

    /// <summary>Parses an expression.</summary>
    /// <param name="text">The Expr source.</param>
    /// <param name="options">Optional parser settings.</param>
    /// <returns>The parsed syntax tree.</returns>
    /// <exception cref="SyntaxException">The source is not valid Expr syntax.</exception>
    public SyntaxTree Parse(string text, SyntaxParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var requestedOptions = options ?? new SyntaxParserOptions();
        ArgumentNullException.ThrowIfNull(requestedOptions.DisabledBuiltins);
        ArgumentNullException.ThrowIfNull(requestedOptions.OverriddenBuiltins);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedOptions.MaximumSourceLength);
        if (requestedOptions.MaximumSourceLength > 0 && text.Length > requestedOptions.MaximumSourceLength)
        {
            throw new SyntaxException(
                $"compilation failed: expression exceeds maximum source length of {requestedOptions.MaximumSourceLength}");
        }

        this.options = requestedOptions with
        {
            DisabledBuiltins = new HashSet<string>(requestedOptions.DisabledBuiltins, StringComparer.Ordinal),
            OverriddenBuiltins = new HashSet<string>(requestedOptions.OverriddenBuiltins, StringComparer.Ordinal),
        };
        source = new SourceText(text);
        tokens = new SyntaxLexer { DisableIfOperator = this.options.DisableIfOperator }.Lex(text);
        index = 0;
        predicateDepth = 0;
        nodeCount = 0;
        parseDepth = 0;

        var root = ParseSequenceExpression();
        if (!Current.Is(TokenKind.EndOfFile))
        {
            Error(Current, $"unexpected token {Current}");
        }

        return new SyntaxTree(root, source);
    }

    /// <summary>Attempts to parse an expression without throwing for a syntax error.</summary>
    /// <param name="text">The Expr source.</param>
    /// <param name="tree">The parsed tree when successful.</param>
    /// <param name="diagnostic">The diagnostic when unsuccessful.</param>
    /// <param name="options">Optional parser settings.</param>
    /// <returns><see langword="true"/> when parsing succeeds.</returns>
    public bool TryParse(
        string text,
        out SyntaxTree? tree,
        out SyntaxDiagnostic? diagnostic,
        SyntaxParserOptions? options = null)
    {
        try
        {
            tree = Parse(text, options);
            diagnostic = null;
            return true;
        }
        catch (SyntaxException exception)
        {
            tree = null;
            diagnostic = exception.Diagnostic;
            return false;
        }
    }

    private SyntaxNode ParseSequenceExpression()
    {
        var nodes = new List<SyntaxNode> { ParseExpression(0) };
        while (Current.Is(TokenKind.Operator, ";"))
        {
            Advance();
            if (Current.Is(TokenKind.EndOfFile))
            {
                break;
            }

            nodes.Add(ParseExpression(0));
        }

        return nodes.Count == 1 ? nodes[0] : Create(new SequenceNode(nodes, nodes[0].Location));
    }

    private SyntaxNode ParseExpression(int precedence)
    {
        parseDepth++;
        if (options.MaximumParseDepth <= 0 || parseDepth > options.MaximumParseDepth)
        {
            Error(Current, $"compilation failed: expression exceeds maximum parse depth of {options.MaximumParseDepth}");
        }

        try
        {
            return ParseExpressionCore(precedence);
        }
        finally
        {
            parseDepth--;
        }
    }

    private SyntaxNode ParseExpressionCore(int precedence)
    {
        if (precedence == 0 && Current.Is(TokenKind.Operator, "let"))
        {
            return ParseVariableDeclaration();
        }

        if (precedence == 0 && !options.DisableIfOperator && Current.Is(TokenKind.Operator, "if"))
        {
            return ParseIfConditional();
        }

        var left = ParsePrimary();
        var previousOperator = string.Empty;
        while (Current.Is(TokenKind.Operator))
        {
            var operatorToken = Current;
            var negate = operatorToken.Value == "not";
            SyntaxToken? notToken = null;
            if (negate)
            {
                Advance();
                if (!AllowedNegateSuffix(Current.Value) ||
                    !BinaryOperators.TryGetValue(Current.Value, out var negatedInfo) ||
                    negatedInfo.Precedence < precedence)
                {
                    index--;
                    break;
                }

                notToken = Current;
                operatorToken = Current;
            }

            if (!BinaryOperators.TryGetValue(operatorToken.Value, out var info) || info.Precedence < precedence)
            {
                break;
            }

            Advance();
            if (operatorToken.Value == "|")
            {
                var identifier = Current;
                Expect(TokenKind.Identifier);
                left = ParseCall(identifier, [left], checkOverrides: true);
                previousOperator = operatorToken.Value;
                continue;
            }

            if (previousOperator == "??" && operatorToken.Value != "??")
            {
                Error(operatorToken, $"Operator ({operatorToken.Value}) and coalesce expressions (??) cannot be mixed. Wrap either by parentheses.");
            }

            if (IsComparison(operatorToken.Value))
            {
                left = ParseComparison(left, operatorToken, info.Precedence);
                previousOperator = operatorToken.Value;
                continue;
            }

            var right = ParseExpression(info.RightAssociative ? info.Precedence : info.Precedence + 1);
            left = Create(new BinaryNode(operatorToken.Value, left, right, operatorToken.Location));
            if (negate)
            {
                left = Create(new UnaryNode("not", left, notToken!.Location));
            }

            previousOperator = operatorToken.Value;
        }

        return precedence == 0 ? ParseTernary(left) : left;
    }

    private SyntaxNode ParseVariableDeclaration()
    {
        Expect(TokenKind.Operator, "let");
        var name = Current;
        Expect(TokenKind.Identifier);
        Expect(TokenKind.Operator, "=");
        var value = ParseExpression(0);
        Expect(TokenKind.Operator, ";");
        var body = ParseSequenceExpression();
        return Create(new VariableDeclaratorNode(name.Value, value, body, name.Location));
    }

    private SyntaxNode ParseIfConditional()
    {
        parseDepth++;
        if (parseDepth > options.MaximumParseDepth)
        {
            Error(Current, $"compilation failed: expression exceeds maximum parse depth of {options.MaximumParseDepth}");
        }

        try
        {
            return ParseIfConditionalCore();
        }
        finally
        {
            parseDepth--;
        }
    }

    private SyntaxNode ParseIfConditionalCore()
    {
        var token = Current;
        Advance();
        var condition = ParseExpression(0);
        Expect(TokenKind.Bracket, "{");
        var whenTrue = ParseSequenceExpression();
        Expect(TokenKind.Bracket, "}");
        Expect(TokenKind.Operator, "else");
        SyntaxNode whenFalse;
        if (Current.Is(TokenKind.Operator, "if"))
        {
            whenFalse = ParseIfConditional();
        }
        else
        {
            Expect(TokenKind.Bracket, "{");
            whenFalse = ParseSequenceExpression();
            Expect(TokenKind.Bracket, "}");
        }

        return Create(new ConditionalNode(condition, whenTrue, whenFalse, false, token.Location));
    }

    private SyntaxNode ParseTernary(SyntaxNode condition)
    {
        while (Current.Is(TokenKind.Operator, "?"))
        {
            var token = Current;
            Advance();
            SyntaxNode whenTrue;
            SyntaxNode whenFalse;
            if (Current.Is(TokenKind.Operator, ":"))
            {
                Advance();
                whenTrue = condition;
                whenFalse = ParseExpression(0);
            }
            else
            {
                whenTrue = ParseExpression(0);
                Expect(TokenKind.Operator, ":");
                whenFalse = ParseExpression(0);
            }

            condition = Create(new ConditionalNode(condition, whenTrue, whenFalse, true, token.Location));
        }

        return condition;
    }

    private SyntaxNode ParsePrimary()
    {
        var token = Current;
        if (token.Is(TokenKind.Operator) && UnaryOperators.TryGetValue(token.Value, out var precedence))
        {
            Advance();
            return ParsePostfix(Create(new UnaryNode(token.Value, ParseExpression(precedence), token.Location)));
        }

        if (token.Is(TokenKind.Bracket, "("))
        {
            Advance();
            var expression = ParseSequenceExpression();
            Expect(TokenKind.Bracket, ")");
            return ParsePostfix(expression);
        }

        if (predicateDepth > 0 && (token.Is(TokenKind.Operator, "#") || token.Is(TokenKind.Operator, ".")))
        {
            var name = string.Empty;
            if (token.Value == "#")
            {
                Advance();
                if (Current.Is(TokenKind.Identifier))
                {
                    name = Current.Value;
                    Advance();
                }
            }
            else
            {
                // The dot is also the postfix member operator for the implicit pointer.
            }

            return ParsePostfix(Create(new PointerNode(name, token.Location)));
        }

        if (token.Is(TokenKind.Operator, "::"))
        {
            Advance();
            token = Current;
            Expect(TokenKind.Identifier);
            return ParsePostfix(ParseCall(token, [], checkOverrides: false));
        }

        return ParseSecondary();
    }

    private SyntaxNode ParseSecondary()
    {
        var token = Current;
        SyntaxNode node;
        switch (token.Kind)
        {
            case TokenKind.Identifier:
                Advance();
                node = token.Value switch
                {
                    "true" => Create(new BooleanNode(true, token.Location)),
                    "false" => Create(new BooleanNode(false, token.Location)),
                    "nil" => Create(new NilNode(token.Location)),
                    _ when Current.Is(TokenKind.Bracket, "(") => ParseCall(token, [], checkOverrides: true),
                    _ => Create(new IdentifierNode(token.Value, token.Location)),
                };
                if (token.Value is "true" or "false" or "nil")
                {
                    return node;
                }

                break;
            case TokenKind.Number:
                Advance();
                return ParseNumber(token);
            case TokenKind.String:
                Advance();
                node = Create(new StringNode(token.Value, token.Location));
                break;
            case TokenKind.Bytes:
                Advance();
                node = Create(new BytesNode(token.BytesValue.Span, token.Location));
                break;
            default:
                if (token.Is(TokenKind.Bracket, "["))
                {
                    node = ParseArray(token);
                }
                else if (token.Is(TokenKind.Bracket, "{"))
                {
                    node = ParseMap(token);
                }
                else
                {
                    Error(token, $"unexpected token {token}");
                    throw new InvalidOperationException("Unreachable after syntax error.");
                }

                break;
        }

        return ParsePostfix(node);
    }

    private SyntaxNode ParseNumber(SyntaxToken token)
    {
        var value = token.Value.Replace("_", string.Empty, StringComparison.Ordinal);
        try
        {
            if (value.Contains('.', StringComparison.Ordinal) || value.Contains('e', StringComparison.OrdinalIgnoreCase))
            {
                var floatNumber = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (double.IsInfinity(floatNumber))
                {
                    Error(token, "float literal is too large");
                }

                return Create(new FloatNode(floatNumber, token.Location));
            }

            var upper = value.ToUpperInvariant();
            var number = upper.StartsWith("0X", StringComparison.Ordinal)
                ? Convert.ToInt64(value[2..], 16)
                : upper.StartsWith("0B", StringComparison.Ordinal)
                    ? Convert.ToInt64(value[2..], 2)
                    : upper.StartsWith("0O", StringComparison.Ordinal)
                        ? Convert.ToInt64(value[2..], 8)
                        : long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return Create(new IntegerNode(number, token.Location));
        }
        catch (FormatException exception)
        {
            Error(token, $"invalid number literal: {exception.Message}");
            throw;
        }
        catch (OverflowException exception)
        {
            Error(token, $"integer literal is too large: {exception.Message}");
            throw;
        }
    }

    private SyntaxNode ParseCall(SyntaxToken token, IEnumerable<SyntaxNode> initial, bool checkOverrides)
    {
        var arguments = initial.ToList();
        var initialArgumentCount = arguments.Count;
        var overridden = checkOverrides && options.OverriddenBuiltins.Contains(token.Value);
        if (Predicates.TryGetValue(token.Value, out var signature) && !overridden)
        {
            Expect(TokenKind.Bracket, "(");
            if (arguments.Count == 0)
            {
                if (Current.Is(TokenKind.Bracket, ")"))
                {
                    Error(Current, $"expected at least {signature.TotalArguments} arguments");
                }

                arguments.Add(ParseExpression(0));
            }

            if (Current.Is(TokenKind.Bracket, ")") && !signature.PredicateOptional)
            {
                Error(Current, $"expected at least {signature.TotalArguments - initialArgumentCount} arguments");
            }

            if (!Current.Is(TokenKind.Bracket, ")"))
            {
                if (initialArgumentCount == 0)
                {
                    Expect(TokenKind.Operator, ",");
                }

                arguments.Add(ParsePredicate());
            }

            if (signature.HasThirdArgument && !Current.Is(TokenKind.Bracket, ")"))
            {
                Expect(TokenKind.Operator, ",");
                arguments.Add(ParseExpression(0));
            }

            if (Current.Is(TokenKind.Operator, ","))
            {
                Advance();
            }

            Expect(TokenKind.Bracket, ")");
            return Create(new BuiltinNode(token.Value, arguments, token.Location));
        }

        var parsedArguments = ParseArguments(arguments);
        if (Builtins.Contains(token.Value) && !options.DisabledBuiltins.Contains(token.Value) && !overridden)
        {
            return Create(new BuiltinNode(token.Value, parsedArguments, token.Location));
        }

        var callee = Create(new IdentifierNode(token.Value, token.Location));
        return Create(new CallNode(callee, parsedArguments, token.Location));
    }

    private IReadOnlyList<SyntaxNode> ParseArguments(List<SyntaxNode> arguments)
    {
        var offset = arguments.Count;
        Expect(TokenKind.Bracket, "(");
        while (!Current.Is(TokenKind.Bracket, ")"))
        {
            if (arguments.Count > offset)
            {
                Expect(TokenKind.Operator, ",");
            }

            if (Current.Is(TokenKind.Bracket, ")"))
            {
                break;
            }

            arguments.Add(ParseExpression(0));
        }

        Expect(TokenKind.Bracket, ")");
        return arguments;
    }

    private SyntaxNode ParsePredicate()
    {
        var startToken = Current;
        var bracketed = Current.Is(TokenKind.Bracket, "{");
        if (bracketed)
        {
            Advance();
        }

        predicateDepth++;
        var body = bracketed ? ParseSequenceExpression() : ParseExpression(0);
        predicateDepth--;
        if (!bracketed && Current.Is(TokenKind.Operator, ";"))
        {
            Error(Current, "wrap predicate with brackets { and }");
        }

        if (bracketed)
        {
            Expect(TokenKind.Bracket, "}");
        }

        return Create(new PredicateNode(body, startToken.Location));
    }

    private SyntaxNode ParseArray(SyntaxToken token)
    {
        var elements = new List<SyntaxNode>();
        Expect(TokenKind.Bracket, "[");
        while (!Current.Is(TokenKind.Bracket, "]"))
        {
            if (elements.Count > 0)
            {
                Expect(TokenKind.Operator, ",");
                if (Current.Is(TokenKind.Bracket, "]"))
                {
                    break;
                }
            }

            elements.Add(ParseExpression(0));
        }

        Expect(TokenKind.Bracket, "]");
        return Create(new ArrayNode(elements, token.Location));
    }

    private SyntaxNode ParseMap(SyntaxToken token)
    {
        var pairs = new List<PairNode>();
        Expect(TokenKind.Bracket, "{");
        while (!Current.Is(TokenKind.Bracket, "}"))
        {
            if (pairs.Count > 0)
            {
                Expect(TokenKind.Operator, ",");
                if (Current.Is(TokenKind.Bracket, "}"))
                {
                    break;
                }

                if (Current.Is(TokenKind.Operator, ","))
                {
                    Error(Current, $"unexpected token {Current}");
                }
            }

            SyntaxNode key;
            if (Current.Kind is TokenKind.Number or TokenKind.String or TokenKind.Identifier)
            {
                key = Create(new StringNode(Current.Value, Current.Location));
                Advance();
            }
            else if (Current.Is(TokenKind.Bracket, "("))
            {
                key = ParseExpression(0);
            }
            else
            {
                Error(Current, $"a map key must be a quoted string, a number, a identifier, or an expression enclosed in parentheses (unexpected token {Current})");
                throw new InvalidOperationException("Unreachable after syntax error.");
            }

            Expect(TokenKind.Operator, ":");
            var value = ParseExpression(0);
            pairs.Add((PairNode)Create(new PairNode(key, value, token.Location)));
        }

        Expect(TokenKind.Bracket, "}");
        return Create(new MapNode(pairs, token.Location));
    }

    private SyntaxNode ParsePostfix(SyntaxNode node)
    {
        while (Current.Is(TokenKind.Operator) || Current.Is(TokenKind.Bracket))
        {
            var postfix = Current;
            var optional = postfix.Value == "?.";
            if (postfix.Value is "." or "?.")
            {
                Advance();
                if (Current.Is(TokenKind.EndOfFile))
                {
                    Error(Current, "unexpected end of expression");
                }

                if (optional && Current.Is(TokenKind.Bracket, "["))
                {
                    postfix = Current;
                    goto ParseBracket;
                }

                var propertyToken = Current;
                Advance();
                if (propertyToken.Kind != TokenKind.Identifier &&
                    (propertyToken.Kind != TokenKind.Operator || !IsValidIdentifier(propertyToken.Value)))
                {
                    Error(propertyToken, "expected name");
                }

                var property = Create(new StringNode(propertyToken.Value, propertyToken.Location));
                var isChain = node is ChainNode;
                if (node is ChainNode chain)
                {
                    node = chain.Expression;
                }

                var member = new MemberNode(node, property, optional, false, propertyToken.Location);
                if (Current.Is(TokenKind.Bracket, "("))
                {
                    member = member with { IsMethod = true };
                    node = Create(new CallNode(Create(member), ParseArguments([]).ToList(), propertyToken.Location));
                }
                else
                {
                    node = Create(member);
                }

                if (isChain || optional)
                {
                    node = Create(new ChainNode(node, propertyToken.Location));
                }

                continue;
            }

        ParseBracket:
            if (postfix.Value == "[")
            {
                Advance();
                SyntaxNode? to = null;
                if (Current.Is(TokenKind.Operator, ":"))
                {
                    Advance();
                    if (!Current.Is(TokenKind.Bracket, "]"))
                    {
                        to = ParseExpression(0);
                    }

                    node = Create(new SliceNode(node, null, to, postfix.Location));
                    Expect(TokenKind.Bracket, "]");
                    continue;
                }

                SyntaxNode? from = ParseExpression(0);
                if (Current.Is(TokenKind.Operator, ":"))
                {
                    Advance();
                    if (!Current.Is(TokenKind.Bracket, "]"))
                    {
                        to = ParseExpression(0);
                    }

                    node = Create(new SliceNode(node, from, to, postfix.Location));
                }
                else
                {
                    node = Create(new MemberNode(node, from, optional, false, postfix.Location));
                    if (optional)
                    {
                        node = Create(new ChainNode(node, postfix.Location));
                    }
                }

                Expect(TokenKind.Bracket, "]");
                continue;
            }

            break;
        }

        return node;
    }

    private SyntaxNode ParseComparison(SyntaxNode left, SyntaxToken token, int precedence)
    {
        SyntaxNode? root = null;
        while (true)
        {
            var comparator = ParseExpression(precedence + 1);
            var comparison = Create(new BinaryNode(token.Value, left, comparator, token.Location));
            root = root is null
                ? comparison
                : Create(new BinaryNode("&&", root, comparison, token.Location));
            left = comparator;
            token = Current;
            if (!token.Is(TokenKind.Operator) || !IsComparison(token.Value))
            {
                return root;
            }

            Advance();
        }
    }

    private T Create<T>(T node)
        where T : SyntaxNode
    {
        nodeCount++;
        if (options.MaximumNodeCount > 0 && nodeCount > options.MaximumNodeCount)
        {
            Error(Current, "compilation failed: expression exceeds maximum allowed nodes");
        }

        return node;
    }

    private void Expect(TokenKind kind, string? value = null)
    {
        if (!Current.Is(kind, value))
        {
            Error(Current, $"unexpected token {Current}");
        }

        Advance();
    }

    private void Advance()
    {
        if (index < tokens.Count - 1)
        {
            index++;
        }
    }

    private void Error(SyntaxToken token, string message)
    {
        var bound = source.Bind(token.Location);
        throw new SyntaxException(new SyntaxDiagnostic(
            message,
            token.Location,
            bound.Line,
            bound.Column,
            bound.FormatSnippet()));
    }

    private SyntaxToken Current => tokens[index];

    private static bool AllowedNegateSuffix(string value) =>
        value is "contains" or "matches" or "startsWith" or "endsWith" or "in";

    private static bool IsComparison(string value) => value is "<" or ">" or ">=" or "<=";

    private static bool IsValidIdentifier(string value)
    {
        var runes = value.EnumerateRunes().ToArray();
        return runes.Length > 0 && IsAlphabetic(runes[0]) && runes.Skip(1).All(IsAlphaNumeric);
    }

    private static bool IsAlphaNumeric(Rune value) => IsAlphabetic(value) || Rune.IsDigit(value);

    private static bool IsAlphabetic(Rune value) => value.Value is '_' or '$' || Rune.IsLetter(value);

    private readonly record struct OperatorInfo(int Precedence, bool RightAssociative);

    private readonly record struct PredicateSignature(bool PredicateOptional, bool HasThirdArgument)
    {
        internal int TotalArguments => HasThirdArgument ? 3 : 2;
    }
}
