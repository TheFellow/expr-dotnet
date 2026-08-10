# Expr parser fuzz harness

This dependency-free harness continuously mutates a small seed set from
`expr-lang/expr/test/fuzz/fuzz_corpus.txt` at upstream commit
`4b31df3a2e0eefec04c017a82a00e0f08541d3e4`. It checks parser termination under
explicit depth/node limits and parse-print-parse canonical and structural
stability. Mutations include arbitrary UTF-16 code units and replacement-decoded
invalid UTF-8 byte sequences.

Run a bounded, reproducible campaign:

```sh
dotnet run --project tools/Expr.Fuzz --configuration Release -- \
  --iterations 100000 --seed c0ffee
```

Pass `--iterations 0` to run until Ctrl+C. A failure reports the seed, iteration,
and the exact source encoded as UTF-16LE base64. Re-run with the reported seed
and at least `iteration + 1` iterations.
