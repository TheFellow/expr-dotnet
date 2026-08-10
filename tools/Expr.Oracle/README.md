# Expr Go oracle

This executable runs expressions with the exact upstream Expr revision pinned
in `go.mod` and emits deterministic, language-neutral JSON Lines results. It is
a development and CI tool, not a runtime dependency of Expr.NET.

```sh
cd tools/Expr.Oracle
go run . < ../../conformance/corpus/upstream.jsonl > /tmp/expr-results.jsonl
```

Each nonblank input line is one `expr.conformance.case/v1` object. Bare protocol
requests may omit `schema`, `expected`, and `provenance`, but must include a
stable `id` and `expression`. Processing errors for an expression are returned
as data, so one bad expression does not stop the stream. Malformed JSON and
unknown request fields stop processing with a nonzero exit code.

Environment numbers with an integer JSON spelling become signed 64-bit values;
numbers containing a decimal point or exponent become binary64. Integers beyond
the signed 64-bit range are rejected. Results normalize all integer widths to a
decimal string, floats to their shortest round-trippable string, bytes to
Base64, and maps to sorted typed entries. See `conformance/README.md` for the
complete contract and its deliberately unsupported host-specific surfaces.
