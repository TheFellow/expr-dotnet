# Architecture

The port follows the same observable compilation pipeline as Expr while using
idiomatic .NET boundaries:

```text
source -> lexer -> parser -> public AST -> binder/type checker
       -> semantic patchers -> optimizer -> bytecode compiler -> VM
```

The public syntax tree retains source locations and supports visitors, walking,
and controlled replacement. Checked programs are immutable and safe to reuse
concurrently with independent evaluation environments.

The VM is the normative execution backend. LINQ expression export, if added,
will be an adapter and will not define language semantics.

