# Port Roadmap

The measurable definition of completion lives in the
[`feature parity contract`](parity.md).

## Foundation

- Public source locations, tokens, AST nodes, visitors, walkers, and patching
- Lexer and Pratt parser with diagnostic parity
- Environment adapters and the checked type model

## Execution

- Checker, overload resolution, functions, and standard built-ins
- Semantic patch passes and optimizer
- Bytecode instruction set, compiler, immutable program, and virtual machine

## Confidence

- Upstream-derived conformance corpus and Go differential oracle
- Parser fuzzing, generated-expression properties, and resource budgets
- BenchmarkDotNet parse, compile, and evaluation suites
- Security threat model, adversarial corpus, and Native AOT smoke application
  (see the [security model](security-model.md))

## Stewardship

- Attractor semport pipeline and append-only upstream commit ledger
- API documentation, examples, package validation, and release automation
