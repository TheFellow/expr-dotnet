# Expr for .NET

Expr is a safe, statically checked expression language for modern .NET. Embed
user-authored rules, filters, policies, and computed values without compiling or
executing arbitrary C#.

- Compile once and evaluate concurrently with isolated runtime state.
- Validate names, types, functions, and return values before execution.
- Work with an immutable, public AST that can be walked, printed, and rewritten.
- Bound evaluation with work, memory, collection, stack, regex, and cancellation limits.
- Run on Native AOT and trimmed applications with reflection-free environment schemas.
- Ship a single library with no third-party runtime dependencies.

Expr targets .NET 10 and C# 14.

## Install

```sh
dotnet add package Expr
```

## Quick start

For a one-off expression with no host environment:

```csharp
using Expr;

object? result = ExprEngine.Evaluate("all([2, 3, 5], # > 0)");
// result is true
```

For repeated evaluation, describe the values visible to the expression, compile
once, and reuse the resulting `CompiledExpression`:

```csharp
using System.Collections.Generic;
using Expr;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Types;

var schema = new ExprEnvironmentSchemaBuilder<OrderContext>()
    .Member("customer", static value => value.Customer, ExprTypes.String)
    .ArrayMember("prices", static value => value.Prices, ExprTypes.Float)
    .Build();

ExprConfiguration configuration = ExprConfiguration.Default
    .WithEnvironment(schema)
    .WithExpectedType(ExprTypes.Boolean);

CompiledExpression expression = ExprEngine.Compile(
    "customer == 'Ada' && sum(prices) >= 100.0",
    configuration);

bool accepted = (bool)expression.Run(
    new OrderContext("Ada", [45.0, 60.0]))!;
// accepted is true

public sealed record OrderContext(
    string Customer,
    IReadOnlyList<double> Prices);
```

The schema is strict: misspelled names and invalid operations fail during
`Compile`, not in a later production evaluation. Explicit schemas are also safe
for Native AOT and trimming. Conventional applications can instead create a
cached reflection-based schema with `ExprEnvironmentSchema.Reflect<T>()`.

## Add application functions

Functions declare their Expr-visible signature independently of their runtime
implementation, so calls remain statically checked:

```csharp
using System;
using Expr;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Types;

var isPreferred = new ExprFunction(
    "isPreferred",
    [new ExprFunctionOverload([ExprTypes.String], ExprTypes.Boolean)],
    static arguments => ((string)arguments[0]!).StartsWith("vip-", StringComparison.Ordinal));

ExprConfiguration configuration = ExprConfiguration.Default
    .WithFunction(isPreferred);

CompiledExpression expression = ExprEngine.Compile(
    "isPreferred('vip-123')",
    configuration);
```

## Inspect and adapt the AST

Parsing, traversal, canonical printing, rewriting, static checking, compilation,
and execution are all public APIs:

```csharp
using System;
using Expr;
using Expr.Syntax;

SyntaxTree tree = ExprEngine.Parse("price * quantity");

foreach (SyntaxNode node in SyntaxWalker.Traverse(tree.Root))
{
    Console.WriteLine(node.GetType().Name);
}

SyntaxNode rewritten = new RenamePrice().Visit(tree.Root);
Console.WriteLine(SyntaxPrinter.Print(rewritten));
// unitPrice * quantity

sealed class RenamePrice : SyntaxRewriter
{
    protected override SyntaxNode VisitNode(SyntaxNode node) =>
        node is IdentifierNode { Name: "price" } identifier
            ? identifier with { Name = "unitPrice" }
            : node;
}
```

`CompiledExpression` exposes its checked `SyntaxTree`, `SemanticModel`, and
immutable bytecode `Program` for integrations that need more than evaluation.

## Bound untrusted evaluations

Every invocation accepts a cancellation token and independent runtime budgets:

```csharp
using System;
using Expr.Execution;

object? result = expression.Run(
    environment,
    new ExprEvaluationOptions
    {
        WorkBudget = 100_000,
        MemoryBudget = 1_000_000,
        MaximumCollectionLength = 10_000,
        RegularExpressionTimeout = TimeSpan.FromMilliseconds(100),
    },
    cancellationToken);
```

See the
[security model](https://github.com/TheFellow/expr-dotnet/blob/main/docs/security-model.md)
before accepting expressions from an untrusted boundary.

## Where to go next

- [Expr language definition](https://expr-lang.org/docs/language-definition/)
  covers literals, operators, collections, predicates, and built-ins.
- [Native AOT sample](https://github.com/TheFellow/expr-dotnet/blob/main/samples/Expr.NativeAot/Program.cs)
  demonstrates a fully reflection-free schema and explicit evaluation budgets.
- [Compatibility policy](https://github.com/TheFellow/expr-dotnet/blob/main/docs/compatibility.md)
  describes language and .NET integration behavior.
- [Security model](https://github.com/TheFellow/expr-dotnet/blob/main/docs/security-model.md)
  and [security review](https://github.com/TheFellow/expr-dotnet/blob/main/docs/security-review.md)
  document the trust boundary and deployment contract.
- [Architecture](https://github.com/TheFellow/expr-dotnet/blob/main/docs/architecture.md)
  explains the parser-to-bytecode pipeline.
- [Benchmarks](https://github.com/TheFellow/expr-dotnet/blob/main/docs/benchmarks.md)
  documents the reproducible performance suite.

The project is pre-1.0 while its public API matures. Compatibility with the Expr
language is continuously checked against its upstream test corpus.

## Build and contribute

```sh
dotnet restore expr-dotnet.slnx
dotnet format expr-dotnet.slnx --verify-no-changes --no-restore
dotnet build expr-dotnet.slnx --configuration Release --no-restore
dotnet test expr-dotnet.slnx --configuration Release --no-build --no-restore
```

See [CONTRIBUTING.md](https://github.com/TheFellow/expr-dotnet/blob/main/CONTRIBUTING.md)
for the development workflow. Maintainers can find the publication process in
[docs/releasing.md](https://github.com/TheFellow/expr-dotnet/blob/main/docs/releasing.md).

## License

MIT. See [LICENSE](https://github.com/TheFellow/expr-dotnet/blob/main/LICENSE)
and [NOTICE](https://github.com/TheFellow/expr-dotnet/blob/main/NOTICE).
