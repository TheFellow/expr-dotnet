# Final Security Review

Review date: 2026-08-09

Reviewed scope: the public parse, check, optimize, compile, and evaluation
pipeline; standard built-ins; runtime collections and environment schemas;
bytecode validation; diagnostics; serialization; regular expressions; and the
Native AOT deployment path.

Upstream reference: `expr-lang/expr` at
`4b31df3a2e0eefec04c017a82a00e0f08541d3e4`.

## Verdict

Expr.NET's current alpha is suitable for evaluating untrusted expression text
when the host follows the supported deployment contract below. The release
controls in `security-model.md` are implemented and covered by adversarial
tests: source, syntax, checker, execution, collection, regular-expression, and
serialization limits are explicit; bytecode is validated before execution;
reflection metadata and unbound CLR member discovery are denied; evaluation
state is isolated; and diagnostics do not stringify arbitrary host values.

This is a library security review, not a process-isolation claim. Host accessors
and custom functions are trusted application code. The interpreter cannot
preempt a callback while that callback is running, account for memory retained
by the host, or make a mutable host collection thread-safe.

## Supported deployment contract

For untrusted expressions, a host must:

1. retain the default source, syntax, checker, work, memory, stack, scope,
   collection, serialization, and regular-expression limits or replace them
   with deliberate finite limits;
2. use an explicit `ExprEnvironmentSchemaBuilder<TEnvironment>` schema and
   explicit `ExprTypeDescriptor` values;
3. expose only trusted accessors, functions, object types, and read-only
   collection views;
4. pass a cancellation token for request-scoped interruption; and
5. treat reflection-backed schemas, inferred CLR types, dynamic collection
   discovery, and arbitrary host callbacks as trusted-host features rather than
   sandbox primitives.

Setting a memory or syntax limit to zero deliberately disables that limit.
Increasing limits also increases the maximum work or allocation accepted from
one expression.

## Evidence

The standalone `Expr.Security.Tests` project exercises the public API and the
same production assembly shipped to consumers. Its corpus includes:

- deep and wide parser inputs, malformed UTF-16 and escapes, numeric overflow,
  unterminated comments, parser reuse, culture changes, and concurrent parses;
- iterative traversal of a 25,000-level tree and bounded recursive rewriting;
- exact memory-budget boundaries, instruction-work exhaustion, cancellation,
  stack and collection limits, predicate iteration, and hostile collection
  counts;
- malformed opcodes, operands, jumps, source ranges, stacks, scopes, constants,
  variables, calls, and profile boundaries;
- constant and dynamic regular expressions, rejected backreferences, pattern
  length limits, non-backtracking execution, and explicit timeouts;
- explicit-schema allowlists, ignored members, forbidden `Type` and reflection
  metadata, non-strict dictionary lookup, denied unbound CLR discovery, and
  repeated dynamic lookup misses;
- arbitrary-host-value diagnostic and disassembly paths that prove `ToString()`
  is neither called nor disclosed;
- cyclic and deeply nested equality, serialization cycles, JSON depth and
  allocation limits, output escaping amplification, malformed Base64, and
  rejection of POCO serialization without invoking getters; and
- immutable snapshots, live-adapter boundaries, schema reuse, and concurrent
  compiled-expression evaluation.

The main suite additionally covers all 84 VM opcodes, every predicate family,
all standard built-ins, optimized/unoptimized equivalence, deterministic
generated-expression properties, and pinned upstream differential cases.

## Control disposition

| Threat-model control | Disposition | Evidence and boundary |
| --- | --- | --- |
| Source, parser, and checker exhaustion | Closed | Finite source, token, node, parse-depth, and check-depth limits reject before later pipeline stages. Deep/wide and malformed inputs have adversarial coverage. |
| Evaluation allocation exhaustion | Closed | VM collection and string allocations are preflighted; allocating built-ins have finite internal limits, pre-invocation estimators, and VM charges. Budget units are conservative semantic units, not exact CLR heap bytes. Environment inputs are not charged. |
| Infinite interpreter execution | Closed | Every instruction consumes work, backward jumps and host-call boundaries observe cancellation, and stack/scope/collection growth is bounded. A running host callback remains outside interpreter control. |
| Catastrophic regular expressions | Closed | Constant and dynamic patterns use culture-invariant `RegexOptions.NonBacktracking`, finite pattern limits, and a timeout. Unsupported backreferences are rejected. |
| Reflection escape | Closed for the supported contract | Runtime `any` fetches do not discover CLR members, non-strict roots require dictionary/map lookup, `get` does not reflect over host objects, serialization accepts Expr values rather than POCO discovery, and `Type`/reflection metadata is forbidden. Explicit object descriptors and reflected schemas authorize their declared public surface by host choice. |
| Host mutation | Closed within the interpreter | There is no assignment syntax and Expr-owned syntax/collection values snapshot constructor input. Generic read-only adapters are live views, so mutation and synchronization of the underlying host object remain host responsibilities. |
| Ambient-state surprises | Closed with documented inputs | Numeric conversion is invariant and string rules are ordinal. Clock and timezone are injectable; using the system clock or local mutable host state is an explicit host choice. |
| Numeric edge cases | Closed | Overflow, NaN/infinity, divide/modulo-by-zero, shifts, range sizing, and host conversions have direct or differential coverage. |
| Serialization amplification | Closed | JSON depth, input, materialization, and UTF-8 output are budgeted; cycles and non-finite values are rejected; arbitrary CLR objects and reflection metadata are not materialized. |
| Error disclosure | Closed | Source diagnostics include the expression and location, but runtime values use safe primitive formatting or type-only placeholders. Adversarial `ToString()` regressions cover evaluation and disassembly. |
| Concurrency corruption | Closed within documented ownership | Compiled programs, configurations, schemas, and metadata are immutable; VM stacks and counters are per invocation. Environment and callback thread safety belongs to the host. |
| Malformed bytecode | Closed at the public evaluator | The evaluator validates all 84 opcode forms and rejects malformed metadata/control state deterministically. The security contract still treats programs as same-version compiler products, not an interchange format. |

## Native AOT boundary

The supported Native AOT path compiles and evaluates source through
`ExprEngine` rather than limiting the smoke test to syntax/runtime helpers. The
sample:

- builds a strict typed schema with explicit scalar, array, and map descriptors;
- compiles an expression using comparisons, a string operator, a predicate,
  collection indexing, and repeated environment access;
- evaluates it with explicit work, memory, stack, scope, collection, regex, and
  cancellation settings; and
- validates the result and resource measurements in the native executable.

The AOT-safe path does not call `ExprEnvironmentSchema.Reflect`, inferred
`Member` overloads, `ExprTypes.FromClrType`, or `ExprDynamicCollections`.
Those APIs retain honest trimming/dynamic-code annotations for conventional
managed applications. Narrow analyzer suppressions inside CLR member discovery
are guarded by a runtime rejection when dynamic code is unavailable; they do
not make that discovery path part of the Native AOT contract.

## Dependency and package review

The `Expr` runtime project has no `PackageReference` and therefore no
third-party runtime dependency graph. Test and benchmark dependencies do not
flow into the package. Packing is validated after a Release build, and the AOT
sample references the project directly so it exercises the same production
assembly.

## Residual risks

- Accessors and custom functions can allocate, block, mutate state, perform I/O,
  or ignore cancellation. Only their declared/estimated result charge is visible
  to the VM.
- Work and memory budgets are deterministic interpreter accounting, not OS-level
  CPU or heap quotas. Applications needing containment against trusted-host bugs
  should add process isolation and request deadlines.
- Read-only CLR adapters do not snapshot their source. Concurrent host mutation
  can change results or surface host collection exceptions.
- Reflection-backed schema/type-discovery APIs depend on metadata preservation
  and are intentionally outside the supported trimmed/AOT path.
- .NET's non-backtracking regular-expression engine is the safety boundary. Its
  accepted syntax is intentionally narrower than the full .NET backtracking
  engine and may differ at Expr/RE2 compatibility edges.
- Continuous fuzzing and upstream differential refreshes remain ongoing release
  engineering activities; a green bounded corpus is evidence, not proof that no
  parser/compiler defect exists.

## Validation transcript

The final gate uses .NET SDK 10.0.302 on macOS x64:

```sh
dotnet restore expr-dotnet.slnx
dotnet format expr-dotnet.slnx --verify-no-changes --no-restore
dotnet build expr-dotnet.slnx --configuration Release --no-restore
dotnet test expr-dotnet.slnx --configuration Release --no-build --no-restore
dotnet pack src/Expr/Expr.csproj --configuration Release --no-build \
  --output artifacts/packages

dotnet publish samples/Expr.NativeAot/Expr.NativeAot.csproj \
  --configuration Release --runtime osx-x64 --self-contained true \
  --output <temporary-directory>
<temporary-directory>/Expr.NativeAot
```

The final solution run passed 667 main tests and all 64 standalone adversarial
security tests. The NuGet package contained only its metadata, MIT license,
README, `net10.0` assembly, and XML documentation; `dotnet list package
--include-transitive` reported no packages for the production project.

The Native AOT publish produced a 4,709,032-byte Mach-O x64 executable which
printed:

```text
Expr.NativeAot evaluation passed: 30 instructions, 47 work units, 0 memory units.
```

Publish produced no IL2xxx trimming or IL3xxx AOT diagnostics. The local linker
reported only macOS minimum-version mismatches for Homebrew OpenSSL and Brotli
dylibs; those are host toolchain warnings, not managed trimming/AOT findings or
Expr package dependencies.
