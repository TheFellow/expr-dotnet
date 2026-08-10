# Expr for .NET

Expr is an idiomatic C# semantic port of
[`expr-lang/expr`](https://github.com/expr-lang/expr): a safe, statically checked
expression language designed to compile once and evaluate many times.

The library targets .NET 10 and C# 14, has no third-party runtime dependencies,
and exposes the full pipeline: parsing, public immutable syntax trees, static
checking, semantic patching, optimization, bytecode compilation, and bounded
virtual-machine evaluation.

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

ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
CompiledExpression expression = ExprEngine.Compile(
    "customer == 'Ada' && sum(prices) >= 100.0",
    configuration);

object? result = expression.Run(new OrderContext("Ada", [45.0, 60.0]));
// result is true

public sealed record OrderContext(string Customer, IReadOnlyList<double> Prices);
```

Build schemas explicitly for Native AOT and trimming. Reflection-based schema
discovery is available for conventional applications and clearly annotated at
the API boundary.

## Inspect and adapt the AST

The parsed and optimized trees remain first-class library artifacts. Consumers
can walk, print, or immutably replace nodes before compilation:

```csharp
using System.Collections.Generic;
using Expr;
using Expr.Syntax;

SyntaxTree tree = ExprEngine.Parse("price * quantity");
var visited = new List<SyntaxNode>();
SyntaxWalker.Walk(tree.Root, visited.Add);

SyntaxNode replacement = new IntegerNode(42, tree.Root.Location);
CompiledExpression expression = ExprEngine.Compile(
    new SyntaxTree(replacement, tree.Source));
```

`CompiledExpression` exposes its final `SyntaxTree`, `SemanticModel`, and
immutable bytecode `Program` for advanced integrations.

## Compatibility and confidence

- Semantics are checked against a pinned upstream Go revision by a differential
  oracle, with optimization enabled and disabled.
- All 71 upstream built-ins and all 84 VM opcodes have direct coverage.
- Deterministic generated-expression properties and a standalone fuzz harness
  protect parsing, printing, and optimizer equivalence.
- The complete pinned generated suite (43,689 expressions) and CrowdSec suite
  (673 expressions) compile and run as executable parity tests.
- Evaluation has explicit instruction, memory, stack, call-depth, regex, and
  cancellation controls.
- Release builds enforce nullable analysis, all .NET analyzers, formatting,
  XML documentation, package validation, and warnings as errors.
- The Attractor semport pipeline and append-only ledger make upstream changes
  reviewable and repeatable.

The project is pre-1.0 while its public API matures; pinned upstream feature
parity is enforced as a release gate. See the [feature parity contract](docs/parity.md),
[compatibility policy](docs/compatibility.md), [architecture](docs/architecture.md),
and [security model](docs/security-model.md).

## Build

```sh
dotnet restore expr-dotnet.slnx
dotnet format expr-dotnet.slnx --verify-no-changes --no-restore
dotnet build expr-dotnet.slnx --configuration Release --no-restore
dotnet test expr-dotnet.slnx --configuration Release --no-build --no-restore
```

Contribution and semport workflows are documented in [CONTRIBUTING.md](CONTRIBUTING.md)
and [semport/README.md](semport/README.md).

## License

MIT. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
