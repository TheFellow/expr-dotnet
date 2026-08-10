using System;
using System.Collections.Generic;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Syntax;

namespace Expr.Tests.Execution;

internal static class ExecutionTestCompiler
{
    internal static ExprProgram Compile(
        string source,
        ExprConfiguration? configuration = null,
        bool profiling = false)
    {
        ExprConfiguration effective = configuration ?? ExprConfiguration.Default.WithOptimization(false);
        var parserOptions = new SyntaxParserOptions
        {
            MaximumNodeCount = effective.MaximumNodeCount,
            DisableIfOperator = effective.DisableIfOperator,
            DisabledBuiltins = effective.DisabledBuiltins,
            OverriddenBuiltins = new HashSet<string>(effective.Functions.Keys, StringComparer.Ordinal),
        };
        SyntaxTree tree = new SyntaxParser().Parse(source, parserOptions);
        ExprSemanticModel model = new ExprChecker().Check(tree, effective);
        return ExprCompiler.Compile(
            model,
            effective,
            profiling ? new ExprCompilationOptions { EnableProfiling = true } : null);
    }
}
