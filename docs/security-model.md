# Security Model

Expr.NET is designed to evaluate untrusted expression text against a
host-controlled environment. It is not a general C# scripting engine: an
expression must not discover types, load assemblies, construct arbitrary host
objects, or invoke members that the configured environment did not expose.

This is the engineering threat model. Vulnerability reporting instructions are
in the repository [`SECURITY.md`](../SECURITY.md).

## Trust boundaries

- Expression source, literal contents, collection sizes, and regular-expression
  patterns are untrusted.
- The application chooses the environment shape, custom functions, compile
  options, budgets, and timezone.
- Values inside an environment may be untrusted application data, but exposing
  an object type or delegate is an authorization decision made by the host.
- Custom functions execute host code and are outside the interpreter sandbox.
  Expr.NET bounds its own work but cannot make an arbitrary delegate terminate.
- A compiled program is trusted only if it was produced by the same compatible
  Expr.NET implementation. There is no untrusted bytecode deserialization API.

## Required controls

| Threat | Required control and evidence |
| --- | --- |
| Parser stack or node exhaustion | Configurable node and nesting limits, iterative traversal where practical, adversarial deep/wide source tests |
| Evaluation allocation exhaustion | Per-evaluation work/allocation budget covering ranges, arrays, maps, predicates, string growth, JSON, Base64, and collection built-ins |
| Infinite interpreter execution | No unbounded language loops or recursion; backward bytecode jumps are structurally bounded; cancellation checked at bounded intervals |
| Catastrophic regular expressions | Go-compatible syntax subset, non-backtracking .NET execution, explicit timeout as defense in depth, compile and runtime attack corpus |
| Reflection escape | Cached allowlisted descriptors only; no access to `Type`, reflection APIs, indexers, constructors, static members, non-public members, or arbitrary extension methods |
| Host mutation | Member access is read-only; no assignment syntax; collection results are newly owned or read-only views; document any custom function side effects as host behavior |
| Ambient-state surprises | Ordinal string rules where Expr requires them, invariant numeric conversion, explicit timezone, injectable clock where determinism matters |
| Numeric edge cases | Checked host conversions, defined divide/modulo-by-zero behavior, NaN/infinity corpus, range-size validation before allocation |
| Serialization amplification | Explicit JSON depth and budget accounting; no polymorphic host-type materialization; recursion and output-size tests |
| Error disclosure | Diagnostics include expression source locations but never dump arbitrary environment objects, delegate targets, or private reflection details |
| Concurrency corruption | Immutable compiled programs and metadata; all evaluation stacks, counters, cancellation, and temporary state are per invocation |

## API posture

The safe default configuration retains upstream's 10,000-node compile limit and
1,000,000-unit evaluation memory budget. Disabling or substantially increasing
a limit is an explicit host decision. Public APIs should accept a
`CancellationToken` for cooperative interruption, while budgets remain the
deterministic protection against expensive expressions.

Environment discovery is type-based and cached outside the evaluation hot
path. The cache must not capture environment instances or unbounded,
application-controlled keys. Native AOT compatibility is verified separately;
reflection annotations must be honest about members that a host asks Expr.NET
to discover.

## Release security gate

A stable release requires:

1. threat-model review against the actual public API and bytecode;
2. parser and evaluator adversarial tests for every control above;
3. fuzzing with crashes, hangs, uncontrolled allocation, and differential
   mismatches treated as failures;
4. dependency and package-content review (the runtime library has no
   third-party dependencies);
5. Native AOT smoke tests and trim-analysis review; and
6. a documented response for every intentional difference from Go Expr's RE2,
   Unicode, time, integer, reflection, and collection behavior.
