# Security Review: Syntax and Runtime Foundations

Review date: 2026-08-09

Reviewed port scope: `Expr.Syntax`, `Expr.Runtime`, and `Expr.Types`

Upstream reference: `expr-lang/expr` at `4b31df3a2e0eefec04c017a82a00e0f08541d3e4`

## Verdict

The syntax and runtime foundation has concrete controls for parser exhaustion,
safe AST traversal, bounded deep equality, immutable input snapshots, explicit
environment member exposure, invariant numeric parsing, and value-safe error
messages. The narrow Native AOT smoke vertical now registers and reads typed
environment members, adapts generic read-only collections, performs deep
equality, and publishes and executes without trim or AOT warnings.

This is **not** a release security sign-off. The evaluator, bytecode verifier,
work and allocation budget, cancellation, regular-expression engine, and JSON
limits are not implemented in this reviewed vertical. Their release gates
remain open.

## Evidence added by this review

The isolated `tests/Expr.Security.Tests` project exercises deterministic
adversarial boundaries without intentionally risking `StackOverflowException`
or out-of-memory termination:

- parse depth is rejected at 32 levels and a 2,048-element array is rejected at
  128 nodes;
- malformed UTF-16, Unicode escapes, numeric literals, and comments produce
  structured syntax diagnostics;
- a 25,000-level AST is walked iteratively, while the recursive rewriter rejects
  at its configured depth;
- deep equality terminates on cycles and rejects at its 10,000-level limit;
- hostile generic collections prove metadata and keyed lookup do not enumerate
  unnecessarily;
- syntax nodes and Expr-owned collections snapshot mutable constructor input;
- reflection-free schemas expose only explicitly registered members, and the
  reflection-based schema excludes non-public, static, indexer, and ignored
  members;
- culture, independent parser concurrency, shared immutable tree traversal, and
  schema reuse are covered; and
- runtime diagnostics do not invoke arbitrary host `ToString()` methods or
  include environment values.

The zero-package `samples/Expr.NativeAot` application registers four real typed
environment members with explicit Expr type descriptors, reads those members,
parses and walks syntax, adapts `IReadOnlyList<long>` and
`IReadOnlyDictionary<string, long>` without reflection, and runs
`ExprValue.Equal` over both adapted collections. No warning was suppressed to
obtain a green publish.

## Control disposition

| Threat-model control | Status for reviewed scope | Evidence and residual work |
| --- | --- | --- |
| Parser stack or node exhaustion | Closed for parser entry points | `SyntaxParserOptions` enforces node and parse-depth limits; deep and wide adversarial tests cover rejection. Hosts can explicitly disable the node limit with zero. |
| Evaluation allocation exhaustion | Open | No evaluator-wide work/allocation meter exists yet. Expr-owned arrays/maps snapshot inputs, but that is not an execution budget. Range, collection, string, Base64, JSON, and predicate charges still require evaluator tests. |
| Infinite interpreter execution | Open | There is no reviewed VM in this vertical. Cancellation and structurally bounded backward jumps require separate evidence. |
| Catastrophic regular expressions | Open | No regex evaluator has been reviewed. RE2-subset validation, non-backtracking execution, timeout defense, and an attack corpus are still required. |
| Reflection escape | Partially closed | Explicitly typed schema members and generic collection wrappers are reflection-free and verified under Native AOT. Inferred member types, reflected schemas, and `ExprDynamicCollections` are distinct APIs with honest trim/dynamic-code annotations. Reflected schemas omit non-public/static/indexer/ignored members. Reflected public members returning `Type` or reflection objects are not yet rejected. |
| Host mutation | Partially closed | Syntax containers, byte literals, `ExprArray`, and `ExprMap` defensively copy constructor inputs. CLR collection adapters are live read-only access views, and custom function side effects have not been reviewed. |
| Ambient-state surprises | Partially closed | Numeric syntax parsing is invariant-culture and string equality is ordinal. Timezone, clock injection, and evaluator-wide culture behavior remain open. |
| Numeric edge cases | Partially closed | Strict conversion and NaN ordering behavior have foundation tests. Checked host conversions, arithmetic overflow, divide/modulo by zero, and range allocation require compiler/VM evidence. |
| Serialization amplification | Open | JSON evaluation depth, type-materialization, recursion, and output-size controls do not yet exist in the reviewed surface. |
| Error disclosure | Partially closed | Adversarial objects are not stringified and unknown-member errors omit environment values. Some runtime errors include CLR full type names; the final diagnostic policy must decide whether those names are acceptable. |
| Concurrency corruption | Partially closed | Independent parsers, immutable syntax trees, and immutable schemas are exercised concurrently. `SyntaxParser` itself is stateful and is not claimed to be safe for simultaneous calls. Compiled-program/evaluator isolation remains open. |

## Native AOT boundary

The supported AOT path in this vertical is deliberately explicit:

1. build an `ExprEnvironmentSchema` with
   `ExprEnvironmentSchemaBuilder<TEnvironment>.Member` overloads that receive
   explicit `ExprTypeDescriptor` values;
2. adapt statically typed `IReadOnlyList<T>` and
   `IReadOnlyDictionary<TKey, TValue>` values with `ExprCollections.AsArray`
   and `ExprCollections.AsMap`, register them with the typed builder's
   `ArrayMember` and `MapMember` conveniences, or use Expr-owned snapshot
   collections;
3. parse and walk syntax through public APIs; and
4. use core value operations, including bounded deep equality, over those
   explicitly adapted values.

`ExprEnvironmentSchema.Reflect` is correctly annotated as requiring dynamic
code and unreferenced metadata. The two-argument `Member` overload performs CLR
type inference and is annotated as requiring unreferenced metadata; the
three-argument overload has no reachability edge to that discovery path.
`ExprDynamicCollections` preserves runtime discovery for conventional hosts and
is annotated as requiring dynamic code and unreferenced metadata. Its factory
caches and `MakeGenericMethod` calls are not reachable from the core
`ExprCollections`, `ExprValue.Equal`, or explicit generic adapters.

Raw custom types that implement only generic `IReadOnlyList<T>` or
`IReadOnlyDictionary<TKey, TValue>` are intentionally not auto-discovered by the
AOT-safe `TryAsArray` and `TryAsMap` methods. They must be wrapped explicitly or
registered with `ArrayMember`/`MapMember`. Automatic adaptation remains
available through the dynamic-only `ExprDynamicCollections` API.

## Upstream security corpus basis

The review used the upstream `TestMaxNodes`, `TestMemoryBudget`, builtin
recursion tests, VM budget tests, and `test/fuzz/FuzzExpr` harness as design
inputs. Upstream fuzzing caps source at 1,000 bytes and executes with a 500,000
unit memory budget. Those evaluator properties are targets for later parity,
not evidence that this port already implements them.

## Remaining release blockers

- implement and adversarially validate evaluator work/allocation budgets and
  cancellation;
- validate bytecode control flow and per-invocation state isolation;
- constrain regex syntax/execution and run catastrophic-pattern tests;
- add JSON depth/output/type-materialization gates;
- prohibit reflection-object discovery or explicitly narrow and document the
  reflected schema contract;
- keep reflection-discovered host values outside the supported trimmed/AOT
  contract unless a future source-generated registration mechanism is added;
- fuzz lexer, parser, checker, optimizer, compiler, and VM with timeout and
  allocation monitoring; and
- review the final package contents and dependency graph.

## Validation transcript

The following commands passed on .NET SDK 10.0.302 and macOS x64:

```sh
dotnet restore tests/Expr.Security.Tests/Expr.Security.Tests.csproj
dotnet format tests/Expr.Security.Tests/Expr.Security.Tests.csproj --verify-no-changes --no-restore
dotnet build tests/Expr.Security.Tests/Expr.Security.Tests.csproj --configuration Release --no-restore
dotnet test tests/Expr.Security.Tests/Expr.Security.Tests.csproj --configuration Release --no-build

dotnet restore samples/Expr.NativeAot/Expr.NativeAot.csproj
dotnet format samples/Expr.NativeAot/Expr.NativeAot.csproj --verify-no-changes --no-restore
dotnet build samples/Expr.NativeAot/Expr.NativeAot.csproj --configuration Release --no-restore
dotnet publish samples/Expr.NativeAot/Expr.NativeAot.csproj \
  --configuration Release --runtime osx-x64 --no-restore --output <temporary-directory>
<temporary-directory>/Expr.NativeAot

dotnet pack src/Expr/Expr.csproj --configuration Release --no-build --output artifacts/packages
```

All 28 security tests passed. The two concurrency/reuse tests passed in ten
consecutive filtered runs. The 2,075,968-byte Mach-O x64 AOT binary printed
`Expr.NativeAot smoke passed: 7 nodes, 4 members.` The publish produced no
IL2xxx trim or IL3xxx AOT diagnostics. The local linker did emit macOS minimum
version warnings for Homebrew OpenSSL and Brotli libraries; these are toolchain
environment warnings rather than managed trim-analysis findings.
