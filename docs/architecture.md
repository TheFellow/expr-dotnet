# Architecture

The port follows the same observable compilation pipeline as Expr while using
idiomatic .NET boundaries:

```text
source -> lexer -> parser -> public AST -> binder/type checker
       -> semantic patchers -> optimizer -> bytecode compiler -> VM
```

The public syntax tree retains source locations and supports visitors, walking,
and non-mutating replacement. Its sealed node records expose strongly typed,
immutable children so consumers can use exhaustive pattern matching to build
formatters, analyzers, policy translators, or conservative LINQ pushdown
adapters without depending on compiler or VM internals. Checked programs are
immutable and safe to reuse concurrently with independent evaluation
environments.

The VM is the normative execution backend. LINQ expression export, if added,
will be an adapter and will not define language semantics.
