# Deliberate skip breadcrumbs

An `acknowledged` ledger event must point to
`semport/skipped/<full-sha>.md`. The file records:

- the full SHA, subject, author, and upstream date;
- every upstream path changed;
- why the change has no C# semantic effect; and
- the evidence inspected, including related Expr.NET behavior or tests.

“Go-specific” is not sufficient by itself. The explanation must show why no
observable Expr behavior, public AST contract, diagnostic, test corpus, or
performance/security property needs to change.
