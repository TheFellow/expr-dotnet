# Compatibility

The compatibility target is the upstream revision recorded in
[`upstream.md`](upstream.md). The Go implementation is the semantic oracle until
Expr publishes a language-independent conformance specification.

Platform differences must be narrow, explicit, and tested. This document will
record differences in host reflection, integer widths, time values, regular
expressions, Unicode handling, and native collection types as each area lands.

## Accepted host-language mappings

- `WithContext` injects a final `CancellationToken` for idiomatic .NET
  functions and methods. A leading token remains supported for direct parity
  with Go's leading `context.Context`; the expression-facing call is identical
  in either form.

No expression-language semantic differences are currently accepted.
