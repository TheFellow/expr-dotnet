# Differential conformance

This directory contains a language-neutral corpus and the Go oracle used to
measure Expr.NET against the pinned upstream implementation. The corpus is an
initial executable inventory, not a claim of complete upstream coverage.

## Protocol

Each line in `corpus/upstream.jsonl` is an independent
`expr.conformance.case/v1` object. Required fields are:

- `id`: stable, unique slash-separated identifier;
- `expression`: Expr source text;
- `provenance`: upstream repository, exact revision, test path, test name, and
  one-based source line;
- `expected`: the normalized oracle outcome checked into the repository.

`operation` defaults to `evaluate`; `compile` records only compile success or a
compile diagnostic. `environment` is JSON-compatible. Integer-spelled JSON
numbers are signed 64-bit integers and decimal/exponent numbers are binary64.
Supported `options` mirror portable upstream options: undefined variables,
optimization, short circuit, `if` syntax, built-in enablement, timezone, node
budget, and expected result type.

Normalized values preserve semantic types. Integers and floats are strings so
JSON readers cannot silently round them. Byte strings are Base64. Arrays retain
order. Maps become sorted arrays of typed key/value entries, supporting Expr
maps whose keys are not strings. Diagnostics record their phase, message, rune
span, and one-based line and column when upstream supplies a source location.

The schemas in `schema/` are normative. They intentionally exclude custom Go
functions, structs, methods, channels, contexts, and reflection-specific host
types. Those require equivalent host fixtures in Go and .NET rather than a
fictional JSON ABI.

## Commands

From the repository root, with the pinned upstream checkout at
`inspiration/expr`:

```sh
go test ./...                         # from tools/Expr.Oracle
python3 conformance/scripts/validate.py
python3 conformance/scripts/validate_traceability.py --json
python3 conformance/scripts/refresh_traceability.py --write
python3 conformance/scripts/refresh_expected.py > /tmp/upstream.jsonl
python3 conformance/scripts/refresh_expected.py --write
dotnet test tests/Expr.Tests/Expr.Tests.csproj --configuration Release --filter FullyQualifiedName~Expr.Tests.Conformance
```

`validate.py` checks schema invariants, unique IDs, provenance paths and line
numbers, including exact top-level Go test declarations. It also proves that
`inventory/builtins.json` matches the declaration
order in pinned `builtin.Builtins` and that every registered name has a corpus
call. The same command validates the complete 687-symbol upstream traceability
inventory and reports its explicit gaps. It then executes every case with the
pinned oracle and diffs normalized outcomes. `refresh_expected.py` emits
regenerated JSONL to standard output by default; `--write` performs an atomic
corpus replacement. Review all oracle changes before committing them.

The .NET conformance tests execute every checked-in request through the public
`ExprEngine` pipeline, compare the normalized outcome to the pinned Go result,
and independently prove that optimized and unoptimized execution agree wherever
the case does not explicitly select an optimization mode.

## Coverage and traceability

The differential corpus contains 137 cases: 125 successes and 12 expected failures.
It spans literals, precedence and control flow, JSON-compatible host
environments, access and slicing, predicates, portable configuration, and all
71 built-ins registered by the pinned revision (with `bitnot` also exercised as
a nested call). Cases point back to the upstream test that motivated them.

The corpus is one part of the broader parity evidence. The inventory at
`inventory/upstream-tests.json` maps every named upstream test, example,
benchmark, and fuzz entry point to a differential case, focused .NET test,
benchmark, reviewed platform mapping, or embedded Go support package. The
current pinned inventory covers all 687 entry points with zero explicit gaps;
CI and release builds enforce that state with `--require-no-gaps`.

See `docs/upstream-test-traceability.md` for the disposition counts and the
reviewed .NET mappings. Time-dependent `now()` is compile-only in the
differential corpus; its runtime behavior is covered by the .NET time tests.
