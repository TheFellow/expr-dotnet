using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Expr.Checking;
using Expr.Configuration;
using Expr.Optimization;
using Expr.Syntax;

namespace Expr.Tests.Optimization;

[SuppressMessage("Design", "CA1515", Justification = "Public xUnit test fixtures inherit this shared base across discovery boundaries.")]
public abstract class OptimizerTestBase
{
    protected static ExprSemanticModel Optimize(
        string expression,
        ExprConfiguration? configuration = null)
    {
        ExprConfiguration effective = configuration ?? ExprConfiguration.Default.AllowUndefinedVariables();
        var parserOptions = new SyntaxParserOptions
        {
            DisabledBuiltins = effective.DisabledBuiltins,
            OverriddenBuiltins = new HashSet<string>(effective.Functions.Keys, System.StringComparer.Ordinal),
            DisableIfOperator = effective.DisableIfOperator,
            MaximumNodeCount = effective.MaximumNodeCount,
        };
        SyntaxTree tree = new SyntaxParser().Parse(expression, parserOptions);
        ExprSemanticModel model = new ExprChecker().Check(tree, effective);
        return ExprOptimizer.Optimize(model, effective);
    }
}
