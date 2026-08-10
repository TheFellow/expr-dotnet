# Benchmarks

The benchmark project measures the public syntax pipeline, complete compilation,
and hot execution of representative Expr programs. It uses BenchmarkDotNet and
targets the same `net10.0` framework as the library.

## Workload definitions

- `CompilationBenchmarks.ColdCompilePolicy` compiles a new policy from source on
  every invocation. It covers parse, check, optimize, and bytecode generation.
  "Cold" here means a cold expression lifecycle; the benchmark process and JIT
  are warmed by BenchmarkDotNet.
- `CompilePredicateOptimized` and `CompilePredicateUnoptimized` isolate the cost
  of enabling optimizer passes for the same filter predicate.
- `EvaluationBenchmarks` compile once in `GlobalSetup`, then measure hot VM
  execution. The suite covers scalar policy evaluation, CLR and map member
  access, constant and dynamic regular expressions, a 1,000-element filter,
  filter-length optimization, and filtered mapping.
- `SyntaxBenchmarks` cover lexing, small and predicate-heavy parsing, an
  upstream-inspired comments/escapes/Unicode workload, and AST walking.
- `RuntimeBenchmarks` retain focused host-value equality and collection-adapter
  microbenchmarks.

The fixtures use explicit environment bindings so hot member-access results do
not accidentally measure reflection-based environment discovery.

## Reproducing results

Build Release first and keep the machine otherwise idle:

```shell
dotnet build benchmarks/Expr.Benchmarks/Expr.Benchmarks.csproj -c Release
dotnet run --project benchmarks/Expr.Benchmarks/Expr.Benchmarks.csproj \
  -c Release --no-build -- \
  --filter '*CompilationBenchmarks*' '*EvaluationBenchmarks*' '*SyntaxBenchmarks*' \
  --job Short --memory \
  --artifacts artifacts/benchmarks/local-short
```

Short runs are suitable for smoke tests and directional measurements. Use more
launches and iterations for a release decision:

```shell
dotnet run --project benchmarks/Expr.Benchmarks/Expr.Benchmarks.csproj \
  -c Release --no-build -- \
  --filter '*CompilationBenchmarks*' '*EvaluationBenchmarks*' '*SyntaxBenchmarks*' \
  --memory --launchCount 3 --warmupCount 5 --iterationCount 10 \
  --artifacts artifacts/benchmarks/release
```

To include process startup and JIT effects for the full compile path, run the
single benchmark with the cold-start strategy:

```shell
dotnet run --project benchmarks/Expr.Benchmarks/Expr.Benchmarks.csproj \
  -c Release --no-build -- \
  --filter '*CompilationBenchmarks.ColdCompilePolicy*' \
  --strategy ColdStart --launchCount 10 --warmupCount 0 \
  --iterationCount 1 --invocationCount 1 --unrollFactor 1 --memory \
  --artifacts artifacts/benchmarks/cold-start
```

Do not compare results across machines, runtime versions, power modes, or
BenchmarkDotNet jobs. Preserve the generated report with the source revision and
machine description when using a result as an optimization baseline.

## Initial baseline

This directional baseline was recorded on 2026-08-09 from a worktree based on
revision `8d5fab2`. The machine ran macOS 15.7.8 on an Intel Core i5-1038NG7
(4 physical/8 logical cores), .NET SDK 10.0.302, and .NET 10.0.10 x64 RyuJIT.
BenchmarkDotNet 0.15.8 used one launch, three warmups, and three measured
iterations. It could not elevate process priority. Consequently, results with
large variation, especially `ParsePredicate` and `FilteredMap`, are recorded but
are not strong optimization evidence.

| Area | Workload | Mean | Allocated/op |
| --- | --- | ---: | ---: |
| Compilation | Cold policy compile | 14.770 us | 15,768 B |
| Compilation | Predicate, optimizer off | 9.327 us | 12,808 B |
| Compilation | Predicate, optimizer on | 16.240 us | 19,578 B |
| Evaluation | Policy | 904.7 ns | 920 B |
| Evaluation | CLR member access | 434.1 ns | 680 B |
| Evaluation | Map access | 706.6 ns | 656 B |
| Evaluation | Constant regular expression | 569.3 ns | 632 B |
| Evaluation | Dynamic regular expression | 481.7 ns | 632 B |
| Evaluation | Filter 1,000 values | 186.295 us | 82,688 B |
| Evaluation | Filter length, optimizer on | 188.740 us | 72,736 B |
| Evaluation | Filter length, optimizer off | 196.693 us | 82,744 B |
| Evaluation | Filtered map 1,000 values | 227.968 us | 86,096 B |
| Syntax | Lex small | 690.8 ns | 840 B |
| Syntax | Parse small | 2.294 us | 1,776 B |
| Syntax | Parse predicate | 9.187 us | 5,560 B |
| Syntax | Parse upstream workload | 10.634 us | 6,688 B |
| Syntax | Walk predicate AST | 943.9 ns | 1,048 B |

## Focused VM follow-up

After separating value-only execution from detailed results and making
predicate-scope and profiling containers lazy, a focused ShortRun of
`EvaluationBenchmarks.Policy` recorded 801.7 ns and 536 bytes per operation.
Against the initial 904.7 ns and 920-byte result, that is 384 fewer bytes, or a
41.7% allocation reduction. The mean was directionally 11.4% faster.

The allocation result is the stronger evidence. Both measurements were separate
three-iteration ShortRuns rather than an interleaved statistical comparison. A
second focused run reproduced 536 bytes but had high timing variation, with a
1.493 us mean and 0.556 us standard deviation. Treat the first run's latency
improvement as encouraging, not established; use the multi-launch release
command above before making a timing claim.

The implementation change reflects the public API's intent. Ordinary `Run`
calls now return the value without constructing an `ExprEvaluationResult` or a
profile-sample projection. Detailed execution still produces resource metrics
and profiles when explicitly requested. Scalar programs also avoid allocating
predicate-scope and profiling collections they never use. This retains
diagnostic behavior while removing diagnostic-only work from the common path.

## Evidence-led optimization candidates

1. The focused policy still allocates 536 bytes per hot evaluation. Measure the
   execution-machine object, operand-stack storage, and variable storage before
   choosing the next change. Right-sized frames or carefully cleared pooling
   may help, but any reuse must preserve thread isolation and prevent values
   leaking between tenants.
2. Filtering 1,000 integers allocates about 83 KB. Filter-length fusion removes
   about 10 KB (12%) and was about 4% faster in this short run, but still
   allocates 73 KB. Profile predicate scopes, operand-stack traffic, result
   materialization, and boxed numeric operations before selecting an
   implementation change.
3. Optimized predicate compilation took 16.240 us and 19,578 bytes versus
   9.327 us and 12,808 bytes without optimization: roughly 74% more time and 53%
   more allocation. This is an expected compile-time tradeoff, so applications
   should cache compiled expressions. Optimizer allocation is worth reducing
   only if end-to-end hot evaluation measurements show the optimization pays
   back for realistic reuse counts.
4. AST walking allocates 1,048 bytes despite reusing the visitor. The guarded
   iterator currently creates traversal state, a stack, and a cycle-detection
   set. A dedicated visitor path or reusable traversal state is a measurable
   target, provided depth, node-count, and cycle protections remain intact.

Only the policy workload has been remeasured after the VM change. Refresh the
full suite before using the initial filter, compilation, syntax, or other hot
evaluation numbers to quantify a subsequent change.

These candidates are hypotheses anchored to allocation data, not accepted
regressions or optimization commitments. Add a focused benchmark and preserve
semantic, concurrency, and security tests before changing production code.
