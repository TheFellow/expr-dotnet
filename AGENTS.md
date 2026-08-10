# Expr.NET contributor instructions

Expr.NET is a semantic port of `expr-lang/expr`, not a transliteration. Read the
corresponding upstream implementation and tests before porting behavior.

## Required conventions

- Target .NET 10 and C# 14.
- Keep `src/Expr` free of third-party runtime dependencies.
- Use explicit `using` directives, file-scoped namespaces, nullable annotations,
  braces, immutable public contracts, and culture-explicit conversions.
- Treat analyzer and compiler warnings as errors. Do not suppress a diagnostic
  without documenting why the rule is inapplicable.
- Document every public API with XML comments.
- Prefer sealed records for immutable syntax/value nodes and sealed classes for
  stateful implementation types.
- Preserve Expr semantics even where ordinary C# semantics differ. Record an
  intentional platform difference in `docs/compatibility.md` and cover it with
  tests.
- The public AST must remain walkable and patchable without exposing mutable
  compiler or VM internals.
- Avoid dynamic code generation, reflection on evaluation hot paths, ambient
  culture, unbounded recursion, and regexes without explicit safety controls.
- Use `rg` for repository searches and `apply_patch` for hand-authored edits.

## Validation

Before committing, run:

```sh
dotnet format expr-dotnet.slnx --verify-no-changes --no-restore
dotnet build expr-dotnet.slnx --configuration Release --no-restore
dotnet test expr-dotnet.slnx --configuration Release --no-build
dotnet pack src/Expr/Expr.csproj --configuration Release --no-build --output artifacts/packages
```

Every semantic change requires focused tests. Performance changes require a
benchmark comparison; security-sensitive changes require adversarial tests.

