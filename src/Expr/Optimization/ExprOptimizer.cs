using System;
using System.Collections.Generic;
using Expr.Checking;
using Expr.Configuration;
using Expr.Syntax;
using Expr.Types;

namespace Expr.Optimization;

/// <summary>Applies the ordered Expr optimizer pipeline to a checked expression.</summary>
/// <remarks>
/// The pass order and convergence limits are a semantic port of
/// <c>expr-lang/expr/optimizer.Optimize</c>. The returned model is re-checked because
/// Expr.NET stores types in an immutable side table instead of mutable AST fields.
/// </remarks>
public static class ExprOptimizer
{
    private const int FoldPassLimit = 1_001;
    private const int ConstantFunctionPassLimit = 101;

    /// <summary>Optimizes a checked expression and returns semantics for the resulting immutable tree.</summary>
    /// <param name="model">The checked input expression.</param>
    /// <param name="configuration">The same configuration used to check the input.</param>
    /// <returns>The original model when optimization is disabled or makes no change; otherwise a checked optimized model.</returns>
    /// <exception cref="ExprOptimizationException">A compile-time function fails or a pass does not converge.</exception>
    public static ExprSemanticModel Optimize(ExprSemanticModel model, ExprConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ExprConfiguration effectiveConfiguration = configuration ?? model.Configuration;
        if (!ReferenceEquals(effectiveConfiguration, model.Configuration))
        {
            throw new ArgumentException(
                "The optimization configuration must be the same instance used to check the semantic model.",
                nameof(configuration));
        }
        if (!effectiveConfiguration.Optimize)
        {
            return model;
        }

        SyntaxNode root = model.SyntaxTree.Root;
        root = Apply(new InArrayRewriter(model, effectiveConfiguration.MaximumCheckDepth), root);
        root = ApplyUntilStable(
            static depth => new ConstantFoldRewriter(depth),
            root,
            effectiveConfiguration.MaximumCheckDepth,
            FoldPassLimit,
            "constant folding");

        if (effectiveConfiguration.ConstantFunctions.Count > 0)
        {
            root = ApplyUntilStable(
                depth => new ConstantFunctionRewriter(effectiveConfiguration, depth),
                root,
                effectiveConfiguration.MaximumCheckDepth,
                ConstantFunctionPassLimit,
                "constant function folding");
        }

        root = Apply(new InRangeRewriter(model, effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new FilterMapRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new FilterLengthRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new FilterLastRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new FilterFirstRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new PredicateCombinationRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new SumRangeRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new SumArrayRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new SumMapRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new CountAnyRewriter(effectiveConfiguration.MaximumCheckDepth), root);
        root = Apply(new CountThresholdRewriter(effectiveConfiguration.MaximumCheckDepth), root);

        if (ReferenceEquals(root, model.SyntaxTree.Root))
        {
            return model;
        }

        var optimizedTree = new SyntaxTree(root, model.SyntaxTree.Source);
        ExprSemanticModel checkedModel = new ExprChecker().CheckForOptimization(optimizedTree, effectiveConfiguration);
        return MergeRetainedAnnotations(checkedModel, model);
    }

    private static SyntaxNode Apply(OptimizationRewriter rewriter, SyntaxNode root) => rewriter.Visit(root);

    private static SyntaxNode ApplyUntilStable(
        Func<int, OptimizationRewriter> factory,
        SyntaxNode root,
        int maximumDepth,
        int limit,
        string passName)
    {
        for (var iteration = 0; iteration < limit; iteration++)
        {
            OptimizationRewriter rewriter = factory(maximumDepth);
            SyntaxNode replacement = rewriter.Visit(root);
            root = replacement;
            if (!rewriter.Applied)
            {
                return root;
            }
        }

        OptimizationRewriter probe = factory(maximumDepth);
        SyntaxNode final = probe.Visit(root);
        if (probe.Applied)
        {
            throw new ExprOptimizationException($"{passName} did not converge after {limit} passes.");
        }

        return final;
    }

    private static ExprSemanticModel MergeRetainedAnnotations(
        ExprSemanticModel optimized,
        ExprSemanticModel original)
    {
        var annotations = new Dictionary<SyntaxNode, ExprNodeSemantics>(
            optimized.Annotations,
            ReferenceEqualityComparer.Instance);
        var collector = new RetainedAnnotationCollector(annotations, original);
        SyntaxWalker.Walk(optimized.SyntaxTree.Root, collector);
        var generated = new GeneratedSemanticsCollector(annotations);
        SyntaxWalker.Walk(optimized.SyntaxTree.Root, generated);
        return collector.Added || generated.Changed
            ? new ExprSemanticModel(optimized.SyntaxTree, annotations, optimized.Configuration)
            : optimized;
    }

    private sealed class RetainedAnnotationCollector(
        Dictionary<SyntaxNode, ExprNodeSemantics> annotations,
        ExprSemanticModel original) : ISyntaxVisitor
    {
        public bool Added { get; private set; }

        public void Visit(SyntaxNode node)
        {
            if (!annotations.ContainsKey(node) && original.TryGetSemantics(node, out ExprNodeSemantics? semantics))
            {
                annotations.Add(node, semantics!);
                Added = true;
            }
        }
    }

    private sealed class GeneratedSemanticsCollector(
        Dictionary<SyntaxNode, ExprNodeSemantics> annotations) : ISyntaxVisitor
    {
        public bool Changed { get; private set; }

        public void Visit(SyntaxNode node)
        {
            if (node is not BuiltinNode { Map: not null } builtin ||
                !annotations.TryGetValue(builtin.Map, out ExprNodeSemantics? mapSemantics))
            {
                return;
            }

            ExprTypeDescriptor? generatedType = builtin.Name switch
            {
                "filter" => ExprTypes.ArrayOf(mapSemantics.Type),
                "find" or "findLast" => mapSemantics.Type,
                _ => null,
            };
            if (generatedType is null)
            {
                return;
            }

            annotations.TryGetValue(node, out ExprNodeSemantics? existing);
            annotations[node] = new ExprNodeSemantics(
                generatedType,
                existing?.Function,
                existing?.Overload,
                existing?.Member,
                existing?.ValueConversion);
            Changed = true;
        }
    }
}
