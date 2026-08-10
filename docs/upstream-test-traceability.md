# Upstream test traceability

The machine-readable inventory at
[`conformance/inventory/upstream-tests.json`](../conformance/inventory/upstream-tests.json)
enumerates every top-level `Test*`, `Benchmark*`, `Example*`, and `Fuzz*`
function in every pinned upstream `*_test.go` file. Receiver methods are not Go
test entry points and are intentionally excluded.

The inventory is generated from all 93 `*_test.go` files at upstream revision
`4b31df3a2e0eefec04c017a82a00e0f08541d3e4`. Each symbol has exactly one
disposition, source line, granularity, evidence list, and explanatory note.
The validator proves that the upstream symbol set has not changed, all corpus
IDs point back to that exact symbol, and every linked .NET test, benchmark,
fuzz harness, and documentation anchor still exists.

## Current audit

| Disposition | Tests | Benchmarks | Examples | Fuzzers | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Differential corpus | 25 | 0 | 3 | 0 | 28 |
| Direct .NET test | 256 | 0 | 22 | 1 | 279 |
| BenchmarkDotNet workload | 0 | 55 | 0 | 0 | 55 |
| Reviewed platform mapping | 46 | 0 | 0 | 0 | 46 |
| Embedded Go support package | 258 | 4 | 17 | 0 | 279 |
| **Explicit gap** | **0** | **0** | **0** | **0** | **0** |
| **Total** | **585** | **59** | **42** | **1** | **687** |

All 408 Expr-product symbols remaining after embedded Go support packages are
excluded have executable evidence or a reviewed, tested platform mapping. The
vendored integration fixtures execute all 43,689 generated expressions and all
673 CrowdSec expressions against the public .NET pipeline.

The complete gap list remains in the JSON inventory so CI and release tooling
can consume it without scraping this document. To list it locally:

```sh
python3 -c 'import json; p=json.load(open("conformance/inventory/upstream-tests.json")); print("\n".join("{}::{}".format(r["path"], r["symbol"]) for r in p["symbols"] if r["disposition"] == "gap"))'
```

Two scanned build-tag files, `internal/spew/dumpcgo_test.go` and
`internal/spew/dumpnocgo_test.go`, contain no top-level Go test entry point.
They are recorded in `filesWithoutSymbols` so file-level additions remain
detectable.

## Disposition rules

- `differential_corpus` means a checked-in case runs through both the pinned Go
  oracle and the public .NET pipeline with matching normalized outcomes.
- `dotnet_test` identifies the exact focused .NET regression or, where the Go
  test is table-driven, the focused .NET file that covers that test family.
- `dotnet_benchmark` identifies a corresponding BenchmarkDotNet workload. It
  does not imply that timings are comparable across Go and .NET runtimes.
- `platform_mapping` identifies host behavior that cannot be transliterated
  and links both its reviewed mapping and its .NET tests.
- `excluded_support` is limited by the validator to Expr's embedded copies of
  Go support packages: `testify`, `spew`, `difflib`, and `ring`. Their own unit
  tests do not specify Expr language behavior.
- `gap` is intentionally evidence-free. It remains a release-parity blocker
  until replaced by one of the evidence-bearing dispositions.

## Go pointer dereference suites

Go's `internal/deref` and `test/deref` suites exercise arbitrary pointer chains,
pointer-to-interface combinations, embedded pointer/interface promotion, and nil pointer traversal. CLR object
references do not expose Go pointer depth. Expr for .NET instead tests nullable
references, explicitly registered environment members, generic collection
adapters, and value providers. These 30 named tests are recorded as a reviewed
file-family platform mapping, not as byte-for-byte differential cases. Six
historical issue fixtures exercise the same Go-only promotion behavior and are
mapped individually. `builtin/builtin_test.go::TestBuiltin_with_deref` uses the
same mapping at the built-in boundary and is tracked separately.

## Dynamic host method access

Expr.NET intentionally does not discover methods from an unschematized runtime
object. That restriction prevents arbitrary reflection through values typed as
`any`. Consumers that need host method calls register an explicit typed
environment schema, where public interface methods, nullable arguments, and
variadic methods are covered by focused parity tests. The no-schema branch of
issue 688 is therefore recorded as a reviewed security mapping rather than an
executable dynamic-reflection feature.

## Dynamic host member access

Issue 934 relies on Go's runtime reflection of a value statically typed as
`any`. Expr.NET deliberately refuses to discover either public or nonpublic CLR
members through an unschematized value. The focused security regression proves
that boundary. Applications expose allowed public members through an explicit
typed environment schema, which retains static checking and avoids turning
untrusted expression values into a general reflection surface.

## Go documentation generator

Upstream's `docgen` package emits Markdown from Go reflection metadata. The
.NET package does not reproduce that Go-specific output format. Its observable
contracts map to environment-schema type metadata, explicit resolution of
ambiguous promoted members, dictionary-derived schemas, and compiler-generated
XML documentation. Four focused tests exercise those mappings, including the
ambiguity and map cases, without adding a runtime documentation dependency.

Time and duration tests similarly link to the accepted `TimeSpan`,
`DateTimeOffset`, and `TimeZoneInfo` mappings in
[`compatibility.md`](compatibility.md), including precision and tzdb limits.

## Maintenance

Regenerate and validate after changing the upstream pin, corpus provenance, or
coverage links:

```sh
python3 conformance/scripts/refresh_traceability.py --write
python3 -m unittest discover -s conformance/scripts -p 'test_*.py'
python3 conformance/scripts/validate_traceability.py --json
python3 conformance/scripts/validate.py --skip-oracle
```

`--require-no-gaps` is enforced in conformance CI and tag releases.
