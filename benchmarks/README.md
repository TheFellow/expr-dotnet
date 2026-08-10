# Benchmarks

Run the complete suite from the repository root:

```sh
dotnet run --configuration Release --project benchmarks/Expr.Benchmarks --
```

Use the same machine, power mode, SDK, commit, and BenchmarkDotNet settings for
before/after comparisons. Record the commit SHAs and exported result artifact in
an optimization pull request. A performance change must preserve the differential
conformance corpus and should report time, allocation, and generated-code effects;
a single noisy run is not evidence.

The syntax and runtime benchmarks establish the pre-VM baseline. Compile and
evaluation scenarios will be added with those verticals, including optimized and
unoptimized programs and representative upstream benchmark expressions.
