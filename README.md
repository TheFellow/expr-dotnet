# Expr.NET

Expr.NET is a faithful, safe, and fast semantic port of
[`expr-lang/expr`](https://github.com/expr-lang/expr) for modern .NET.

The project is under active construction. Its goal is full language and API
feature parity expressed through idiomatic C#, including a public walkable AST,
static type checking against .NET environments, optimization, bytecode
compilation, and an allocation-conscious virtual machine.

## Principles

- Semantic compatibility is measured against a pinned upstream Expr revision.
- The runtime package has no third-party dependencies.
- The interpreter is side-effect-free, terminating, and Native AOT compatible.
- Public APIs are documented, nullable-correct, thread-safe where promised, and
  designed for normal C# usage rather than transliterated Go.
- Every upstream semantic change is reviewed through the semport ledger.
- Performance claims require reproducible BenchmarkDotNet evidence.
- Security-sensitive behavior requires adversarial tests and explicit limits.

## Status

The repository and quality gates are established. Language implementation and
the upstream conformance corpus are being ported in vertical slices.

## License

MIT. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

