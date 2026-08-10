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
- `time.Duration` values use `TimeSpan`, so host-visible duration precision is
  100 nanoseconds rather than Go's 1 nanosecond. Parsing truncates only the
  sub-tick remainder.
- Time-zone identifiers and historical transitions come from the host's
  `TimeZoneInfo` data. IANA identifiers are portable on current .NET, while
  Windows aliases and the installed tzdb version remain host properties.
- Unicode classification and case conversion use the Unicode tables shipped
  by the active .NET runtime; Go and .NET can differ when their Unicode table
  versions differ.
- Go strings can contain arbitrary bytes, while .NET strings are Unicode. A
  `fromBase64` result containing invalid UTF-8 uses replacement characters;
  callers that require arbitrary binary values should keep them Base64-encoded
  at the expression boundary.

No expression-language semantic differences are currently accepted.
