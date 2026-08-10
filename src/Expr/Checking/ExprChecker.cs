using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Expr.Configuration;
using Expr.Patching;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Checking;

/// <summary>Infers and validates Expr types without mutating the public syntax tree.</summary>
public sealed class ExprChecker
{
    private readonly Dictionary<SyntaxNode, ExprNodeSemantics> annotations =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, IReadOnlyList<MethodInfo>> methodGroups =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<VariableScope> variables = [];
    private readonly List<PredicateScope> predicates = [];
    private ExprConfiguration configuration = ExprConfiguration.Default;
    private SourceText source = new(string.Empty);
    private ExprCheckDiagnostic? firstDiagnostic;
    private int depth;

    /// <summary>Checks a parsed tree and applies configured semantic patchers.</summary>
    /// <remarks>
    /// CLR object member checking uses a process-wide reflection metadata cache. Trimmed and Native-AOT
    /// applications should prefer maps, primitive values, and explicitly declared host functions.
    /// </remarks>
    /// <param name="tree">The parsed syntax tree.</param>
    /// <param name="configuration">Optional immutable checker configuration.</param>
    /// <returns>The semantic model over the final tree.</returns>
    /// <exception cref="ExprCheckException">Static checking fails.</exception>
    public ExprSemanticModel Check(SyntaxTree tree, ExprConfiguration? configuration = null)
    {
        return CheckCore(tree, configuration ?? ExprConfiguration.Default, validateExpectedType: true);
    }

    internal ExprSemanticModel CheckForOptimization(SyntaxTree tree, ExprConfiguration configuration)
    {
        return CheckCore(tree, configuration, validateExpectedType: false);
    }

    private ExprSemanticModel CheckCore(
        SyntaxTree tree,
        ExprConfiguration configuration,
        bool validateExpectedType)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(configuration);
        this.configuration = configuration;
        ValidateConfiguration(this.configuration);

        SyntaxTree current = tree;
        CheckState state = Build(current);
        for (var pass = 0; pass < 32 && this.configuration.Patchers.Count > 0; pass++)
        {
            var changed = false;
            foreach (IExprSemanticPatcher patcher in this.configuration.Patchers)
            {
                SyntaxNode replacement = patcher.Apply(current.Root, state.Model, this.configuration);
                if (ReferenceEquals(replacement, current.Root))
                {
                    continue;
                }

                current = new SyntaxTree(replacement, current.Source);
                state = Build(current);
                changed = true;
            }

            if (!changed)
            {
                break;
            }

            if (pass is 31)
            {
                throw new InvalidOperationException("Semantic patchers did not converge after 32 passes.");
            }
        }

        if (state.Diagnostic is not null)
        {
            throw new ExprCheckException(state.Diagnostic);
        }

        if (validateExpectedType)
        {
            ValidateExpectedResult(state.Model.ResultType);
        }

        return state.Model;
    }

    /// <summary>Attempts to check a parsed tree without throwing for a checking diagnostic.</summary>
    /// <param name="tree">The parsed syntax tree.</param>
    /// <param name="model">The semantic model when successful.</param>
    /// <param name="diagnostic">The diagnostic when unsuccessful.</param>
    /// <param name="configuration">Optional immutable checker configuration.</param>
    /// <returns><see langword="true"/> when checking succeeds.</returns>
    public bool TryCheck(
        SyntaxTree tree,
        [NotNullWhen(true)] out ExprSemanticModel? model,
        [NotNullWhen(false)] out ExprCheckDiagnostic? diagnostic,
        ExprConfiguration? configuration = null)
    {
        try
        {
            model = Check(tree, configuration);
            diagnostic = null;
            return true;
        }
        catch (ExprCheckException exception)
        {
            model = null;
            diagnostic = exception.Diagnostic;
            return false;
        }
    }

    private CheckState Build(SyntaxTree tree)
    {
        annotations.Clear();
        methodGroups.Clear();
        variables.Clear();
        predicates.Clear();
        source = tree.Source;
        firstDiagnostic = null;
        depth = 0;
        _ = Visit(tree.Root);
        return new CheckState(new ExprSemanticModel(tree, annotations), firstDiagnostic);
    }

    private ExprTypeDescriptor Visit(SyntaxNode node)
    {
        if (++depth > configuration.MaximumCheckDepth)
        {
            depth--;
            return Error(node, $"expression exceeds maximum checker depth of {configuration.MaximumCheckDepth}");
        }

        ExprTypeDescriptor type = node switch
        {
            NilNode => ExprTypes.Nil,
            IdentifierNode identifier => VisitIdentifier(identifier),
            IntegerNode => ExprTypes.Integer,
            FloatNode => ExprTypes.Float,
            BooleanNode => ExprTypes.Boolean,
            StringNode => ExprTypes.String,
            BytesNode => ExprTypes.ArrayOf(ExprTypes.Integer),
            ConstantNode constant => ExprTypes.FromRuntimeValue(constant.Value),
            UnaryNode unary => VisitUnary(unary),
            BinaryNode binary => VisitBinary(binary),
            ChainNode chain => Visit(chain.Expression),
            MemberNode member => VisitMember(member),
            SliceNode slice => VisitSlice(slice),
            CallNode call => VisitCall(call),
            BuiltinNode builtin => VisitBuiltin(builtin),
            PredicateNode predicate => VisitPredicate(predicate),
            PointerNode pointer => VisitPointer(pointer),
            ConditionalNode conditional => VisitConditional(conditional),
            VariableDeclaratorNode variable => VisitVariable(variable),
            SequenceNode sequence => VisitSequence(sequence),
            ArrayNode array => VisitArray(array),
            MapNode map => VisitMap(map),
            PairNode pair => VisitPair(pair),
            _ => Error(node, $"undefined syntax node type {node.GetType().FullName}"),
        };

        if (node is IdentifierNode or MemberNode &&
            configuration.Patchers.Any(static patcher => patcher is ValueProviderPatcher) &&
            type is ObjectTypeDescriptor objectType &&
            ClrTypeModel.Get(objectType.ClrType).ValueProviderType is ExprTypeDescriptor convertedType)
        {
            annotations.TryGetValue(node, out ExprNodeSemantics? existing);
            annotations[node] = new ExprNodeSemantics(
                convertedType,
                existing?.Function,
                existing?.Overload,
                existing?.Member,
                new ExprValueConversionBinding(convertedType));
            type = convertedType;
        }
        else
        {
            annotations.TryAdd(node, new ExprNodeSemantics(type));
        }
        depth--;
        return type;
    }

    private ExprTypeDescriptor VisitIdentifier(IdentifierNode node)
    {
        for (var index = variables.Count - 1; index >= 0; index--)
        {
            if (string.Equals(variables[index].Name, node.Name, StringComparison.Ordinal))
            {
                return variables[index].Type;
            }
        }

        if (string.Equals(node.Name, "$env", StringComparison.Ordinal))
        {
            return ExprTypes.Any;
        }

        if (configuration.Environment?.TryGetMember(node.Name, out ExprEnvironmentMember? member) is true &&
            member is not null)
        {
            annotations[node] = new ExprNodeSemantics(
                member.Type,
                Member: new ExprMemberBinding(node.Name, ExprMemberBindingKind.Environment));
            return member.Type;
        }

        if (configuration.Functions.TryGetValue(node.Name, out ExprFunction? function))
        {
            ExprTypeDescriptor functionType = FunctionType(function);
            annotations[node] = new ExprNodeSemantics(functionType, function);
            return functionType;
        }

        if (configuration.Environment is not null)
        {
            ClrTypeModel environmentModel = ClrTypeModel.Get(configuration.Environment.EnvironmentType);
            if (environmentModel.Methods.TryGetValue(node.Name, out IReadOnlyList<MethodInfo>? methods))
            {
                methodGroups[node] = methods;
                ExprTypeDescriptor type = MethodType(methods[0]);
                annotations[node] = new ExprNodeSemantics(
                    type,
                    Member: new ExprMemberBinding(node.Name, ExprMemberBindingKind.ClrMethod, methods[0]));
                return type;
            }
        }

        if (!configuration.Strict)
        {
            return ExprTypes.Any;
        }

        if (!configuration.DisabledBuiltins.Contains(node.Name) &&
            configuration.Builtins.TryGetValue(node.Name, out ExprFunction? builtin))
        {
            ExprTypeDescriptor functionType = FunctionType(builtin);
            annotations[node] = new ExprNodeSemantics(functionType, builtin);
            return functionType;
        }

        return Error(node, $"unknown name {node.Name}");
    }

    private ExprTypeDescriptor VisitUnary(UnaryNode node)
    {
        ExprTypeDescriptor operand = Visit(node.Operand);
        return node.Operator switch
        {
            "!" or "not" when operand.Kind is ExprTypeKind.Boolean || ExprTypeRelations.IsUnknown(operand) =>
                ExprTypes.Boolean,
            "+" or "-" when ExprTypeRelations.IsNumber(operand) => operand,
            "+" or "-" when ExprTypeRelations.IsUnknown(operand) => ExprTypes.Any,
            "!" or "not" => Error(node, $"invalid operation: {node.Operator} (mismatched type {operand})"),
            "+" or "-" => Error(node, $"invalid operation: {node.Operator} (mismatched type {operand})"),
            _ => Error(node, $"unknown operator ({node.Operator})"),
        };
    }

    private ExprTypeDescriptor VisitBinary(BinaryNode node)
    {
        ExprTypeDescriptor left = Visit(node.Left);
        ExprTypeDescriptor right = Visit(node.Right);
        ExprTypeDescriptor? result = node.Operator switch
        {
            "==" or "!=" when ExprTypeRelations.Comparable(left, right) => ExprTypes.Boolean,
            "or" or "||" or "and" or "&&" when BothOrUnknown(left, right, ExprTypeKind.Boolean) =>
                ExprTypes.Boolean,
            "<" or ">" or ">=" or "<=" when ComparableOrder(left, right) => ExprTypes.Boolean,
            "-" => Subtraction(left, right),
            "*" => Multiplication(left, right),
            "/" or "**" or "^" when BothNumbersOrUnknown(left, right) => ExprTypes.Float,
            "%" when BothOrUnknown(left, right, ExprTypeKind.Integer) => ExprTypes.Integer,
            "+" => Addition(left, right),
            "in" => CheckIn(node, left, right),
            "matches" => CheckMatches(node, left, right),
            "contains" or "startsWith" or "endsWith" when BothOrUnknown(left, right, ExprTypeKind.String) =>
                ExprTypes.Boolean,
            ".." when BothOrUnknown(left, right, ExprTypeKind.Integer) => ExprTypes.ArrayOf(ExprTypes.Integer),
            "??" => CoalescedType(left, right),
            _ => null,
        };

        if (result is not null)
        {
            return result;
        }

        if (node.Operator is not ("==" or "!=" or "or" or "||" or "and" or "&&" or "<" or ">" or
            ">=" or "<=" or "-" or "*" or "/" or "**" or "^" or "%" or "+" or "in" or
            "matches" or "contains" or "startsWith" or "endsWith" or ".." or "??"))
        {
            return Error(node, $"unknown operator ({node.Operator})");
        }

        return Error(node, $"invalid operation: {node.Operator} (mismatched types {left} and {right})");
    }

    private static ExprTypeDescriptor CoalescedType(ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (left.Kind is ExprTypeKind.Nil && right.Kind is not ExprTypeKind.Nil)
        {
            return right;
        }

        if (right.Kind is ExprTypeKind.Nil && left.Kind is not ExprTypeKind.Nil)
        {
            return left;
        }

        if (ExprTypeRelations.IsUnknown(left) || ExprTypeRelations.IsUnknown(right))
        {
            return ExprTypes.Any;
        }

        return StrictlyAssignable(right, left) ? left : ExprTypes.Any;
    }

    private ExprTypeDescriptor VisitMember(MemberNode node)
    {
        if (node.Target is IdentifierNode { Name: "$env" })
        {
            _ = Visit(node.Target);
            if (node.Property is not StringNode environmentName)
            {
                // Upstream deliberately leaves computed $env indexes dynamic. Resolving the
                // key as a top-level identifier here would reject optional accesses such as
                // $env?.[missing] before the runtime can apply map-default semantics.
                return ExprTypes.Any;
            }

            _ = Visit(node.Property);

            if (configuration.Environment?.TryGetMember(environmentName.Value, out ExprEnvironmentMember? member) is true &&
                member is not null)
            {
                annotations[node] = new ExprNodeSemantics(
                    member.Type,
                    Member: new ExprMemberBinding(environmentName.Value, ExprMemberBindingKind.Environment));
                return member.Type;
            }

            return configuration.Strict && !node.Optional
                ? Error(node, $"unknown name {environmentName.Value}")
                : ExprTypes.Any;
        }

        ExprTypeDescriptor target = Visit(node.Target);
        ExprTypeDescriptor property = Visit(node.Property);
        if (ExprTypeRelations.IsUnknown(target))
        {
            return ExprTypes.Any;
        }

        if (target.Kind is ExprTypeKind.Nil && node.Property is StringNode nilName)
        {
            return Error(node, $"type nil has no field {nilName.Value}");
        }

        if (target is MapTypeDescriptor map)
        {
            if (!ExprTypeRelations.CanAssign(property, map.KeyType))
            {
                return Error(node.Property, $"cannot use {property} to get an element from {map}");
            }

            if (node.Property is StringNode mapName)
            {
                if (map.TryGetField(mapName.Value, out ExprTypeDescriptor? fieldType))
                {
                    annotations[node] = new ExprNodeSemantics(
                        fieldType!,
                        Member: new ExprMemberBinding(mapName.Value, ExprMemberBindingKind.Index));
                    return fieldType!;
                }

                if (map.IsStrict && !node.Optional)
                {
                    return Error(node.Property, $"unknown field {mapName.Value}");
                }
            }

            return map.AdditionalValueType ?? ExprTypes.Any;
        }

        if (target is ArrayTypeDescriptor array)
        {
            if (property.Kind is not ExprTypeKind.Integer && !ExprTypeRelations.IsUnknown(property))
            {
                return Error(node.Property, $"array elements can only be selected using an integer (got {property})");
            }

            annotations[node] = new ExprNodeSemantics(
                array.ElementType,
                Member: new ExprMemberBinding(string.Empty, ExprMemberBindingKind.Index));
            return array.ElementType;
        }

        if (target is ObjectTypeDescriptor objectType && node.Property is StringNode objectName)
        {
            ClrTypeModel model = ClrTypeModel.Get(objectType.ClrType);
            if (model.Methods.TryGetValue(objectName.Value, out IReadOnlyList<MethodInfo>? methods))
            {
                methodGroups[node] = methods;
                ExprTypeDescriptor methodType = MethodType(methods[0]);
                annotations[node] = new ExprNodeSemantics(
                    methodType,
                    Member: new ExprMemberBinding(objectName.Value, ExprMemberBindingKind.ClrMethod, methods[0]));
                return methodType;
            }

            if (model.Members.TryGetValue(objectName.Value, out ClrValueMember? clrMember))
            {
                annotations[node] = new ExprNodeSemantics(
                    clrMember.Type,
                    Member: new ExprMemberBinding(objectName.Value, ExprMemberBindingKind.ClrMember, clrMember.Member));
                return clrMember.Type;
            }

            string memberKind = node.IsMethod ? "method" : "field";
            return Error(node, $"type {objectType} has no {memberKind} {objectName.Value}");
        }

        if (node.Property is StringNode name)
        {
            string memberKind = node.IsMethod ? "method" : "field";
            return Error(node, $"type {target} has no {memberKind} {name.Value}");
        }

        return Error(node, $"type {target}[{property}] is undefined");
    }

    private ExprTypeDescriptor VisitSlice(SliceNode node)
    {
        ExprTypeDescriptor target = Visit(node.Target);
        if (ExprTypeRelations.IsUnknown(target))
        {
            // Bounds are intentionally unchecked when the host value is dynamic; this
            // mirrors upstream and avoids resolving expressions that may never execute.
            return ExprTypes.Any;
        }

        if (node.From is not null)
        {
            ExprTypeDescriptor from = Visit(node.From);
            if (from.Kind is not ExprTypeKind.Integer && !ExprTypeRelations.IsUnknown(from))
            {
                return Error(node.From, $"non-integer slice index {from}");
            }
        }

        if (node.To is not null)
        {
            ExprTypeDescriptor to = Visit(node.To);
            if (to.Kind is not ExprTypeKind.Integer && !ExprTypeRelations.IsUnknown(to))
            {
                return Error(node.To, $"non-integer slice index {to}");
            }
        }

        return target.Kind is ExprTypeKind.String or ExprTypeKind.Array
            ? target
            : Error(node, $"cannot slice {target}");
    }

    private ExprTypeDescriptor VisitCall(CallNode node)
    {
        if (node.Callee is IdentifierNode { Name: "$env" })
        {
            return Error(node, $"{configuration.Environment?.EnvironmentType.FullName ?? "environment"} is not callable");
        }

        ExprTypeDescriptor callee = Visit(node.Callee);
        annotations.TryGetValue(node.Callee, out ExprNodeSemantics? calleeSemantics);

        if (ExprTypeRelations.IsUnknown(callee) && calleeSemantics?.Function is null)
        {
            // Dynamic callables are validated by the runtime. Upstream does not walk
            // their arguments during checking because no signature is available.
            return ExprTypes.Any;
        }

        ExprTypeDescriptor[] argumentTypes = node.Arguments.Select(Visit).ToArray();

        if (calleeSemantics?.Function is ExprFunction function)
        {
            return CheckFunctionCall(node, function, argumentTypes);
        }

        if (methodGroups.TryGetValue(node.Callee, out IReadOnlyList<MethodInfo>? methods))
        {
            return CheckMethodCall(node, methods, argumentTypes);
        }

        if (callee is FunctionTypeDescriptor functionType)
        {
            string name = CallName(node.Callee);
            if (!AcceptsArity(functionType.Parameters.Count, functionType.IsVariadic, argumentTypes.Length))
            {
                return Error(node, ArityMessage(name, functionType.Parameters.Count, functionType.IsVariadic, argumentTypes.Length));
            }

            for (var index = 0; index < argumentTypes.Length; index++)
            {
                ExprTypeDescriptor parameter = ParameterAt(functionType.Parameters, functionType.IsVariadic, index);
                if (!ExprTypeRelations.CanAssign(argumentTypes[index], parameter))
                {
                    return Error(
                        node.Arguments[index],
                        $"cannot use {argumentTypes[index]} as argument (type {parameter}) to call {name}");
                }
            }

            return functionType.ReturnType;
        }

        return Error(node, $"{callee} is not callable");
    }

    private ExprTypeDescriptor VisitBuiltin(BuiltinNode node)
    {
        if (configuration.DisabledBuiltins.Contains(node.Name))
        {
            foreach (SyntaxNode argument in node.Arguments)
            {
                _ = Visit(argument);
            }

            return Error(node, $"unknown builtin {node.Name}");
        }

        if (configuration.Functions.TryGetValue(node.Name, out ExprFunction? overridden))
        {
            return CheckFunctionCall(node, overridden, node.Arguments.Select(Visit).ToArray());
        }

        return node.Name switch
        {
            "all" or "none" or "any" or "one" => CheckPredicateBuiltin(node, PredicateResult.Boolean),
            "filter" => CheckPredicateBuiltin(node, PredicateResult.Collection),
            "map" => CheckPredicateBuiltin(node, PredicateResult.MappedCollection),
            "count" => CheckPredicateBuiltin(node, PredicateResult.Count),
            "sum" => CheckPredicateBuiltin(node, PredicateResult.Sum),
            "find" or "findLast" => CheckPredicateBuiltin(node, PredicateResult.Element),
            "findIndex" or "findLastIndex" => CheckPredicateBuiltin(node, PredicateResult.Count),
            "groupBy" => CheckPredicateBuiltin(node, PredicateResult.Grouped),
            "sortBy" => CheckPredicateBuiltin(node, PredicateResult.Collection),
            "reduce" => CheckPredicateBuiltin(node, PredicateResult.Reduced),
            "get" => CheckGet(node),
            "len" => CheckLen(node),
            "type" => CheckUnaryBuiltin(node, ExprTypes.String),
            "abs" => CheckNumericBuiltin(node),
            "ceil" or "floor" or "round" => CheckNumericFloatBuiltin(node),
            "int" or "bitnot" => CheckUnaryBuiltin(node, ExprTypes.Integer),
            "float" => CheckUnaryBuiltin(node, ExprTypes.Float),
            "string" or "trim" or "trimPrefix" or "trimSuffix" or "upper" or "lower" =>
                CheckDeclaredBuiltin(node, ExprTypes.String),
            "now" or "date" => CheckDeclaredBuiltin(node, ExprTypes.Time),
            "duration" => CheckDeclaredBuiltin(node, ExprTypes.Duration),
            _ => CheckDeclaredBuiltin(node, null),
        };
    }

    private ExprTypeDescriptor CheckPredicateBuiltin(BuiltinNode node, PredicateResult result)
    {
        if (node.Arguments.Count is 0)
        {
            return Error(node, "invalid number of arguments (expected at least 1, got 0)");
        }

        ExprTypeDescriptor collection = Visit(node.Arguments[0]);
        ArrayTypeDescriptor? array = collection as ArrayTypeDescriptor;
        if (array is null && !ExprTypeRelations.IsUnknown(collection))
        {
            return Error(node.Arguments[0], $"builtin {node.Name} takes only array (got {collection})");
        }

        if (node.Arguments.Count is 1)
        {
            if (node.Name is "count")
            {
                return ExprTypes.Integer;
            }

            if (node.Name is "sum")
            {
                return array?.ElementType ?? ExprTypes.Any;
            }

            return Error(node, $"invalid number of arguments for {node.Name}");
        }

        ExprTypeDescriptor accumulator = node.Arguments.Count > 2
            ? Visit(node.Arguments[2])
            : ExprTypes.Any;
        var scopeVariables = new Dictionary<string, ExprTypeDescriptor>(StringComparer.Ordinal)
        {
            ["index"] = ExprTypes.Integer,
        };
        if (node.Name is "reduce")
        {
            scopeVariables["acc"] = ExprTypes.Any;
        }

        predicates.Add(new PredicateScope(array?.ElementType ?? ExprTypes.Any, scopeVariables));
        ExprTypeDescriptor predicate = Visit(node.Arguments[1]);
        predicates.RemoveAt(predicates.Count - 1);
        ExprTypeDescriptor predicateResult = predicate is FunctionTypeDescriptor function
            ? function.ReturnType
            : ExprTypes.Any;

        if (result is PredicateResult.Boolean or PredicateResult.Collection or PredicateResult.Count or PredicateResult.Element &&
            node.Name is not ("map" or "sum" or "groupBy" or "sortBy" or "reduce") &&
            predicateResult.Kind is not ExprTypeKind.Boolean && !ExprTypeRelations.IsUnknown(predicateResult))
        {
            return Error(node.Arguments[1], $"predicate should return boolean (got {predicateResult})");
        }

        if (node.Name is "sortBy" && node.Arguments.Count > 2 &&
            accumulator.Kind is not ExprTypeKind.String && !ExprTypeRelations.IsUnknown(accumulator))
        {
            return Error(node.Arguments[2], $"sortBy order argument must be a string (got {accumulator})");
        }

        return result switch
        {
            PredicateResult.Boolean => ExprTypes.Boolean,
            PredicateResult.Collection => collection,
            PredicateResult.MappedCollection => ExprTypes.ArrayOf(predicateResult),
            PredicateResult.Count => ExprTypes.Integer,
            PredicateResult.Sum => predicateResult,
            PredicateResult.Element => array?.ElementType ?? ExprTypes.Any,
            PredicateResult.Grouped => new MapTypeDescriptor(
                [],
                ExprTypes.ArrayOf(ExprTypes.Any),
                ExprTypes.Any),
            PredicateResult.Reduced => predicateResult,
            _ => ExprTypes.Any,
        };
    }

    private ExprTypeDescriptor CheckGet(BuiltinNode node)
    {
        if (node.Arguments.Count is not 2)
        {
            foreach (SyntaxNode argument in node.Arguments)
            {
                _ = Visit(argument);
            }

            return Error(node, $"invalid number of arguments (expected 2, got {node.Arguments.Count})");
        }

        ExprTypeDescriptor target = Visit(node.Arguments[0]);
        ExprTypeDescriptor index = Visit(node.Arguments[1]);
        if (target is ArrayTypeDescriptor array)
        {
            return index.Kind is ExprTypeKind.Integer || ExprTypeRelations.IsUnknown(index)
                ? array.ElementType
                : Error(node.Arguments[1], $"non-integer slice index {index}");
        }

        if (target is MapTypeDescriptor map)
        {
            if (!ExprTypeRelations.CanAssign(index, map.KeyType))
            {
                return Error(node.Arguments[1], $"cannot use {index} to get an element from {map}");
            }

            if (node.Arguments[1] is StringNode name && map.TryGetField(name.Value, out ExprTypeDescriptor? field))
            {
                return field!;
            }

            return map.AdditionalValueType ?? ExprTypes.Any;
        }

        return ExprTypeRelations.IsUnknown(target)
            ? ExprTypes.Any
            : Error(node.Arguments[0], $"type {target} does not support indexing");
    }

    private ExprTypeDescriptor CheckLen(BuiltinNode node)
    {
        if (node.Arguments.Count is not 1)
        {
            return CheckDeclaredBuiltin(node, ExprTypes.Integer);
        }

        ExprTypeDescriptor argument = Visit(node.Arguments[0]);
        return argument.Kind is ExprTypeKind.String or ExprTypeKind.Array or ExprTypeKind.Map ||
            ExprTypeRelations.IsUnknown(argument)
            ? ExprTypes.Integer
            : Error(node, $"invalid argument for len (type {argument})");
    }

    private ExprTypeDescriptor CheckNumericBuiltin(BuiltinNode node)
    {
        if (node.Arguments.Count is not 1)
        {
            return CheckDeclaredBuiltin(node, null);
        }

        ExprTypeDescriptor argument = Visit(node.Arguments[0]);
        return ExprTypeRelations.IsNumber(argument) || ExprTypeRelations.IsUnknown(argument)
            ? argument
            : Error(node, $"invalid argument for {node.Name} (type {argument})");
    }

    private ExprTypeDescriptor CheckNumericFloatBuiltin(BuiltinNode node)
    {
        ExprTypeDescriptor argument = CheckNumericBuiltin(node);
        return ExprTypeRelations.IsUnknown(argument) ? ExprTypes.Any : ExprTypes.Float;
    }

    private ExprTypeDescriptor CheckUnaryBuiltin(BuiltinNode node, ExprTypeDescriptor result)
    {
        if (node.Arguments.Count is not 1)
        {
            return CheckDeclaredBuiltin(node, result);
        }

        _ = Visit(node.Arguments[0]);
        return result;
    }

    private ExprTypeDescriptor CheckDeclaredBuiltin(BuiltinNode node, ExprTypeDescriptor? fallback)
    {
        ExprTypeDescriptor[] arguments = node.Arguments.Select(Visit).ToArray();
        if (!configuration.DisabledBuiltins.Contains(node.Name) &&
            configuration.Builtins.TryGetValue(node.Name, out ExprFunction? function))
        {
            return CheckFunctionCall(node, function, arguments);
        }

        if (fallback is not null)
        {
            return fallback;
        }

        return Error(node, $"unknown builtin {node.Name}");
    }

    private ExprTypeDescriptor VisitPredicate(PredicateNode node)
    {
        ExprTypeDescriptor body = Visit(node.Body);
        return new FunctionTypeDescriptor([ExprTypes.Any], body);
    }

    private ExprTypeDescriptor VisitPointer(PointerNode node)
    {
        if (predicates.Count is 0)
        {
            return Error(node, "cannot use pointer accessor outside predicate");
        }

        PredicateScope scope = predicates[^1];
        if (node.Name.Length is 0)
        {
            return scope.ElementType;
        }

        return scope.Variables.TryGetValue(node.Name, out ExprTypeDescriptor? type)
            ? type
            : Error(node, $"unknown pointer #{node.Name}");
    }

    private ExprTypeDescriptor VisitConditional(ConditionalNode node)
    {
        ExprTypeDescriptor condition = Visit(node.Condition);
        if (condition.Kind is not ExprTypeKind.Boolean && !ExprTypeRelations.IsUnknown(condition))
        {
            return Error(node.Condition, $"non-bool expression (type {condition}) used as condition");
        }

        ExprTypeDescriptor whenTrue = Visit(node.WhenTrue);
        ExprTypeDescriptor whenFalse = Visit(node.WhenFalse);
        if (whenTrue.Kind is ExprTypeKind.Nil && whenFalse.Kind is not ExprTypeKind.Nil)
        {
            return whenFalse;
        }

        if (whenFalse.Kind is ExprTypeKind.Nil && whenTrue.Kind is not ExprTypeKind.Nil)
        {
            return whenTrue;
        }

        if (!StrictlyAssignable(whenTrue, whenFalse))
        {
            return ExprTypes.Any;
        }

        if (whenTrue is ArrayTypeDescriptor trueArray && whenFalse is ArrayTypeDescriptor falseArray &&
            (!StrictlyAssignable(trueArray.ElementType, falseArray.ElementType) ||
             !StrictlyAssignable(falseArray.ElementType, trueArray.ElementType)))
        {
            return ExprTypes.ArrayOf(ExprTypes.Any);
        }

        return whenTrue;
    }

    private ExprTypeDescriptor VisitVariable(VariableDeclaratorNode node)
    {
        if (configuration.Environment?.Members.ContainsKey(node.Name) is true)
        {
            return Error(node, $"cannot redeclare {node.Name}");
        }

        if (configuration.Functions.ContainsKey(node.Name))
        {
            return Error(node, $"cannot redeclare function {node.Name}");
        }

        if (!configuration.DisabledBuiltins.Contains(node.Name) && configuration.Builtins.ContainsKey(node.Name))
        {
            return Error(node, $"cannot redeclare builtin {node.Name}");
        }

        if (variables.Any(variable => string.Equals(variable.Name, node.Name, StringComparison.Ordinal)))
        {
            return Error(node, $"cannot redeclare variable {node.Name}");
        }

        ExprTypeDescriptor value = Visit(node.Value);
        variables.Add(new VariableScope(node.Name, value));
        ExprTypeDescriptor body = Visit(node.Body);
        variables.RemoveAt(variables.Count - 1);
        return body;
    }

    private static bool StrictlyAssignable(ExprTypeDescriptor value, ExprTypeDescriptor target)
    {
        if (ExprTypeRelations.IsUnknown(value) || ExprTypeRelations.IsUnknown(target))
        {
            return false;
        }

        if (value.Equals(target))
        {
            return true;
        }

        return value is ObjectTypeDescriptor valueObject &&
            target is ObjectTypeDescriptor targetObject &&
            targetObject.ClrType.IsAssignableFrom(valueObject.ClrType);
    }

    private ExprTypeDescriptor VisitSequence(SequenceNode node)
    {
        if (node.Expressions.Count is 0)
        {
            return Error(node, "empty sequence expression");
        }

        ExprTypeDescriptor result = ExprTypes.Nil;
        foreach (SyntaxNode expression in node.Expressions)
        {
            result = Visit(expression);
        }

        return result;
    }

    private ExprTypeDescriptor VisitArray(ArrayNode node)
    {
        if (node.Elements.Count is 0)
        {
            return ExprTypes.ArrayOf(ExprTypes.Any);
        }

        ExprTypeDescriptor element = Visit(node.Elements[0]);
        bool sameKind = true;
        for (var index = 1; index < node.Elements.Count; index++)
        {
            ExprTypeDescriptor current = Visit(node.Elements[index]);
            sameKind &= current.Kind == element.Kind;
            element = current;
        }

        return ExprTypes.ArrayOf(sameKind ? element : ExprTypes.Any);
    }

    private ExprTypeDescriptor VisitMap(MapNode node)
    {
        foreach (PairNode pair in node.Pairs)
        {
            _ = VisitPair(pair);
        }

        return new MapTypeDescriptor([], ExprTypes.Any, ExprTypes.Any);
    }

    private ExprTypeDescriptor VisitPair(PairNode node)
    {
        _ = Visit(node.Key);
        _ = Visit(node.Value);
        return ExprTypes.Nil;
    }

    private ExprTypeDescriptor CheckFunctionCall(
        SyntaxNode node,
        ExprFunction function,
        IReadOnlyList<ExprTypeDescriptor> arguments)
    {
        if (function.TypeValidator is not null)
        {
            try
            {
                ExprTypeDescriptor result = function.TypeValidator(arguments.ToArray());
                annotations[node] = new ExprNodeSemantics(result, function);
                return result;
            }
            catch (ArgumentException exception)
            {
                return Error(node, exception.Message);
            }
            catch (ExprRuntimeException exception)
            {
                return Error(node, exception.Message);
            }
        }

        ExprFunctionOverload? selected = SelectOverload(function.Overloads, arguments);
        if (selected is null)
        {
            annotations[node] = new ExprNodeSemantics(ExprTypes.Any, function);
            return Error(node, FunctionMismatch(function.Name, function.Overloads, arguments, node));
        }

        annotations[node] = new ExprNodeSemantics(selected.ReturnType, function, selected);
        return selected.ReturnType;
    }

    private ExprTypeDescriptor CheckMethodCall(
        CallNode node,
        IReadOnlyList<MethodInfo> methods,
        IReadOnlyList<ExprTypeDescriptor> arguments)
    {
        var candidates = methods.Select(MethodOverload).ToArray();
        ExprFunctionOverload? selected = SelectOverload(candidates, arguments);
        if (selected is null)
        {
            return Error(node, FunctionMismatch(CallName(node.Callee), candidates, arguments, node));
        }

        var selectedIndex = Array.IndexOf(candidates, selected);
        MethodInfo method = methods[selectedIndex];
        if (method.ReturnType == typeof(void))
        {
            return Error(node, $"func {method.Name} doesn't return value");
        }

        annotations[node.Callee] = new ExprNodeSemantics(
            MethodType(method),
            Member: new ExprMemberBinding(method.Name, ExprMemberBindingKind.ClrMethod, method));
        annotations[node] = new ExprNodeSemantics(
            selected.ReturnType,
            Member: new ExprMemberBinding(method.Name, ExprMemberBindingKind.ClrMethod, method));
        return selected.ReturnType;
    }

    private static ExprFunctionOverload? SelectOverload(
        IReadOnlyList<ExprFunctionOverload> overloads,
        IReadOnlyList<ExprTypeDescriptor> arguments)
    {
        ExprFunctionOverload? selected = null;
        var bestScore = -1;
        foreach (ExprFunctionOverload overload in overloads)
        {
            if (!overload.AcceptsArity(arguments.Count))
            {
                continue;
            }

            var score = 0;
            var compatible = true;
            for (var index = 0; index < arguments.Count; index++)
            {
                ExprTypeDescriptor parameter = ParameterAt(overload.Parameters, overload.IsVariadic, index);
                int argumentScore = ExprTypeRelations.MatchScore(arguments[index], parameter);
                if (argumentScore < 0)
                {
                    compatible = false;
                    break;
                }

                score += argumentScore;
            }

            if (compatible && score > bestScore)
            {
                selected = overload;
                bestScore = score;
            }
        }

        return selected;
    }

    private string FunctionMismatch(
        string name,
        IReadOnlyList<ExprFunctionOverload> overloads,
        IReadOnlyList<ExprTypeDescriptor> arguments,
        SyntaxNode node)
    {
        if (overloads.Count is 0)
        {
            return $"no matching overload for {name}";
        }

        int minimum = overloads.Min(static overload =>
            overload.IsVariadic ? overload.Parameters.Count - 1 : overload.Parameters.Count);
        int maximum = overloads.Any(static overload => overload.IsVariadic)
            ? int.MaxValue
            : overloads.Max(static overload => overload.Parameters.Count);
        if (arguments.Count < minimum)
        {
            return $"not enough arguments to call {name}";
        }

        if (arguments.Count > maximum)
        {
            return $"too many arguments to call {name}";
        }

        foreach (ExprFunctionOverload overload in overloads.Where(overload => overload.AcceptsArity(arguments.Count)))
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                ExprTypeDescriptor parameter = ParameterAt(overload.Parameters, overload.IsVariadic, index);
                if (!ExprTypeRelations.CanAssign(arguments[index], parameter))
                {
                    SyntaxNode location = node switch
                    {
                        CallNode call => call.Arguments[index],
                        BuiltinNode builtin => builtin.Arguments[index],
                        _ => node,
                    };
                    Error(location, $"cannot use {arguments[index]} as argument (type {parameter}) to call {name}");
                    return firstDiagnostic!.Message;
                }
            }
        }

        return $"no matching overload for {name}";
    }

    private ExprTypeDescriptor CheckIn(BinaryNode node, ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (right is MapTypeDescriptor map)
        {
            return ExprTypeRelations.CanAssign(left, map.KeyType)
                ? ExprTypes.Boolean
                : Error(node, $"cannot use {left} as type {map.KeyType} in map key");
        }

        if (right is ArrayTypeDescriptor array)
        {
            return ExprTypeRelations.Comparable(left, array.ElementType)
                ? ExprTypes.Boolean
                : Error(node, $"cannot use {left} as type {array.ElementType} in array");
        }

        if (right.Kind is ExprTypeKind.String && ExprTypeRelations.IsUnknown(left))
        {
            return ExprTypes.Boolean;
        }

        if (right is ObjectTypeDescriptor &&
            (left.Kind is ExprTypeKind.String || ExprTypeRelations.IsUnknown(left)) ||
            ExprTypeRelations.IsUnknown(right))
        {
            return ExprTypes.Boolean;
        }

        return null!;
    }

    private ExprTypeDescriptor CheckMatches(BinaryNode node, ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (node.Right is StringNode pattern)
        {
            if (pattern.Value.Length > configuration.MaximumRegularExpressionLength)
            {
                return Error(
                    node.Right,
                    $"regular expression exceeds maximum length of {configuration.MaximumRegularExpressionLength}");
            }

            try
            {
                _ = new Regex(
                    pattern.Value,
                    RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                    configuration.RegularExpressionTimeout);
            }
            catch (ArgumentException exception)
            {
                return Error(node, $"invalid regular expression: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                return Error(node, $"unsupported regular expression: {exception.Message}");
            }
        }

        bool leftMatches = left.Kind is ExprTypeKind.String || node.Left is BytesNode ||
            ExprTypeRelations.IsUnknown(left);
        bool rightMatches = right.Kind is ExprTypeKind.String || ExprTypeRelations.IsUnknown(right);
        return leftMatches && rightMatches ? ExprTypes.Boolean : null!;
    }

    private static ExprTypeDescriptor? Addition(ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (ExprTypeRelations.IsNumber(left) && ExprTypeRelations.IsNumber(right))
        {
            return ExprTypeRelations.PromoteNumber(left, right);
        }

        if (left.Kind is ExprTypeKind.String && right.Kind is ExprTypeKind.String)
        {
            return ExprTypes.String;
        }

        if (left.Kind is ExprTypeKind.Time && right.Kind is ExprTypeKind.Duration ||
            left.Kind is ExprTypeKind.Duration && right.Kind is ExprTypeKind.Time)
        {
            return ExprTypes.Time;
        }

        if (left.Kind is ExprTypeKind.Duration && right.Kind is ExprTypeKind.Duration)
        {
            return ExprTypes.Duration;
        }

        return CompatibleUnknown(left, right, ExprTypeKind.Integer, ExprTypeKind.Float, ExprTypeKind.String,
            ExprTypeKind.Time, ExprTypeKind.Duration)
            ? ExprTypes.Any
            : null;
    }

    private static ExprTypeDescriptor? Subtraction(ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (ExprTypeRelations.IsNumber(left) && ExprTypeRelations.IsNumber(right))
        {
            return ExprTypeRelations.PromoteNumber(left, right);
        }

        if (left.Kind is ExprTypeKind.Time && right.Kind is ExprTypeKind.Time ||
            left.Kind is ExprTypeKind.Duration && right.Kind is ExprTypeKind.Duration)
        {
            return ExprTypes.Duration;
        }

        if (left.Kind is ExprTypeKind.Time && right.Kind is ExprTypeKind.Duration)
        {
            return ExprTypes.Time;
        }

        return CompatibleUnknown(left, right, ExprTypeKind.Integer, ExprTypeKind.Float, ExprTypeKind.Time,
            ExprTypeKind.Duration)
            ? ExprTypes.Any
            : null;
    }

    private static ExprTypeDescriptor? Multiplication(ExprTypeDescriptor left, ExprTypeDescriptor right)
    {
        if (ExprTypeRelations.IsNumber(left) && ExprTypeRelations.IsNumber(right))
        {
            return ExprTypeRelations.PromoteNumber(left, right);
        }

        if (left.Kind is ExprTypeKind.Duration &&
            (ExprTypeRelations.IsNumber(right) || right.Kind is ExprTypeKind.Duration) ||
            right.Kind is ExprTypeKind.Duration && ExprTypeRelations.IsNumber(left))
        {
            return ExprTypes.Duration;
        }

        return CompatibleUnknown(left, right, ExprTypeKind.Integer, ExprTypeKind.Float, ExprTypeKind.Duration)
            ? ExprTypes.Any
            : null;
    }

    private static bool ComparableOrder(ExprTypeDescriptor left, ExprTypeDescriptor right) =>
        ExprTypeRelations.IsNumber(left) && ExprTypeRelations.IsNumber(right) ||
        left.Kind == right.Kind && left.Kind is ExprTypeKind.String or ExprTypeKind.Time or ExprTypeKind.Duration ||
        CompatibleUnknown(left, right, ExprTypeKind.Integer, ExprTypeKind.Float, ExprTypeKind.String,
            ExprTypeKind.Time, ExprTypeKind.Duration);

    private static bool BothNumbersOrUnknown(ExprTypeDescriptor left, ExprTypeDescriptor right) =>
        ExprTypeRelations.IsNumber(left) && ExprTypeRelations.IsNumber(right) ||
        CompatibleUnknown(left, right, ExprTypeKind.Integer, ExprTypeKind.Float);

    private static bool BothOrUnknown(
        ExprTypeDescriptor left,
        ExprTypeDescriptor right,
        ExprTypeKind required) =>
        left.Kind == required && right.Kind == required ||
        CompatibleUnknown(left, right, required);

    private static bool CompatibleUnknown(
        ExprTypeDescriptor left,
        ExprTypeDescriptor right,
        params ExprTypeKind[] allowed)
    {
        bool leftUnknown = ExprTypeRelations.IsUnknown(left);
        bool rightUnknown = ExprTypeRelations.IsUnknown(right);
        return leftUnknown && (rightUnknown || allowed.Contains(right.Kind)) ||
            rightUnknown && allowed.Contains(left.Kind);
    }

    private static FunctionTypeDescriptor FunctionType(ExprFunction function)
    {
        ExprFunctionOverload? first = function.Overloads.Count is 0 ? null : function.Overloads[0];
        return first is null
            ? new FunctionTypeDescriptor([], ExprTypes.Any, true)
            : new FunctionTypeDescriptor(first.Parameters, first.ReturnType, first.IsVariadic);
    }

    private static FunctionTypeDescriptor MethodType(MethodInfo method)
    {
        ExprFunctionOverload overload = MethodOverload(method);
        return new FunctionTypeDescriptor(overload.Parameters, overload.ReturnType, overload.IsVariadic);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Method overload discovery is reachable only after ClrTypeModel rejects Native AOT execution.")]
    private static ExprFunctionOverload MethodOverload(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        ExprTypeDescriptor[] types = parameters
            .Select(static parameter => ExprTypes.FromClrType(parameter.ParameterType.IsArray &&
                parameter.GetCustomAttribute<ParamArrayAttribute>() is not null
                    ? parameter.ParameterType.GetElementType()!
                    : parameter.ParameterType))
            .ToArray();
        bool variadic = parameters.LastOrDefault()?.GetCustomAttribute<ParamArrayAttribute>() is not null;
        ExprTypeDescriptor result = method.ReturnType == typeof(void)
            ? ExprTypes.Nil
            : ExprTypes.FromClrType(method.ReturnType);
        return new ExprFunctionOverload(types, result, variadic);
    }

    private static ExprTypeDescriptor ParameterAt(
        IReadOnlyList<ExprTypeDescriptor> parameters,
        bool variadic,
        int index) => variadic && index >= parameters.Count - 1
            ? parameters[^1]
            : parameters[index];

    private static bool AcceptsArity(int parameterCount, bool variadic, int argumentCount) =>
        variadic ? argumentCount >= parameterCount - 1 : argumentCount == parameterCount;

    private static string ArityMessage(string name, int parameterCount, bool variadic, int argumentCount)
    {
        int minimum = variadic ? parameterCount - 1 : parameterCount;
        return argumentCount < minimum
            ? $"not enough arguments to call {name}"
            : $"too many arguments to call {name}";
    }

    private static string CallName(SyntaxNode callee) => callee switch
    {
        IdentifierNode identifier => identifier.Name,
        MemberNode { Property: StringNode property } => property.Value,
        _ => "function",
    };

    private ExprTypeDescriptor Error(SyntaxNode node, string message)
    {
        firstDiagnostic ??= ExprCheckDiagnostic.Create(message, node, source);
        return ExprTypes.Unknown;
    }

    private void ValidateExpectedResult(ExprTypeDescriptor result)
    {
        ExprTypeDescriptor? expected = configuration.ExpectedType;
        if (expected is null)
        {
            return;
        }

        if (ExprTypeRelations.IsUnknown(result))
        {
            if (configuration.AllowAnyResult)
            {
                return;
            }

            throw new ExprCheckException($"expected {expected}, but got {result}");
        }

        bool accepted = expected.Kind switch
        {
            ExprTypeKind.Integer or ExprTypeKind.Float => ExprTypeRelations.IsNumber(result),
            _ => ExprTypeRelations.CanAssign(result, expected),
        };
        if (!accepted)
        {
            throw new ExprCheckException($"expected {expected}, but got {result}");
        }
    }

    private static void ValidateConfiguration(ExprConfiguration configuration)
    {
        foreach (OperatorOverridePatcher patcher in configuration.Patchers.OfType<OperatorOverridePatcher>())
        {
            foreach (string name in patcher.FunctionNames)
            {
                if (!configuration.Functions.TryGetValue(name, out ExprFunction? function))
                {
                    bool environmentFunction =
                        configuration.Environment?.TryGetMember(name, out ExprEnvironmentMember? member) is true &&
                        member?.Type is FunctionTypeDescriptor functionType &&
                        !functionType.IsVariadic &&
                        functionType.Parameters.Count is 2;
                    if (!environmentFunction)
                    {
                        throw new InvalidOperationException(
                            $"Function {name} for {patcher.OperatorName} operator does not exist in the environment.");
                    }

                    continue;
                }

                if (function.Overloads.Count is 0 || function.Overloads.Any(static overload =>
                    overload.IsVariadic || overload.Parameters.Count is not 2))
                {
                    throw new InvalidOperationException(
                        $"Function {name} for {patcher.OperatorName} operator does not have a correct signature.");
                }
            }
        }
    }

    private sealed record CheckState(ExprSemanticModel Model, ExprCheckDiagnostic? Diagnostic);

    private sealed record VariableScope(string Name, ExprTypeDescriptor Type);

    private sealed record PredicateScope(
        ExprTypeDescriptor ElementType,
        IReadOnlyDictionary<string, ExprTypeDescriptor> Variables);

    private enum PredicateResult
    {
        Boolean,
        Collection,
        MappedCollection,
        Count,
        Sum,
        Element,
        Grouped,
        Reduced,
    }
}
