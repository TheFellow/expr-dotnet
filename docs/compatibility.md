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
  sub-tick remainder. The same 100-nanosecond precision limit applies to
  `DateTime` and `DateTimeOffset` fractional seconds.
- Time-zone identifiers and historical transitions come from the host's
  `TimeZoneInfo` data. IANA identifiers are portable on current .NET, while
  Windows aliases and the installed tzdb version remain host properties.
- Go layouts accept numeric zone offsets with second precision, but
  `DateTimeOffset` stores only whole-minute offsets. Expr.NET preserves the
  instant and returns UTC when the parsed offset contains seconds (or is
  outside `DateTimeOffset`'s fourteen-hour offset range). Unknown `MST`-style
  abbreviations retain Go's fabricated zero-offset behavior; matching a named
  zone abbreviation depends on the names exposed by `TimeZoneInfo`.
- Go can represent calendar year zero. `DateTimeOffset` cannot, so a parsed
  layout that omits the year (including the default time-only layout) maps
  Go's year zero to year one.
- Unicode classification and case conversion use the Unicode tables shipped
  by the active .NET runtime; Go and .NET can differ when their Unicode table
  versions differ.
- Go strings can contain arbitrary bytes, while .NET strings are Unicode. A
  `fromBase64` result containing invalid UTF-8 uses replacement characters;
  callers that require arbitrary binary values should keep them Base64-encoded
  at the expression boundary.
- Unschematized values typed as `any` do not expose arbitrary CLR members or
  methods. Consumers register a typed environment schema when expressions need
  host-object access. This is an intentional security boundary: it prevents an
  expression from walking from an untrusted value into reflection metadata or
  invoking an unexpected host method.
- `get` accepts Expr maps, arrays, and strings. It does not reflect over an
  arbitrary CLR object; expose the desired member through a typed schema or
  adapt the object to an Expr map first.
- `toJSON` accepts Expr scalars, arrays, and maps. It does not enumerate an
  arbitrary object's getters because getters may execute host code. Consumers
  serialize or adapt domain objects explicitly at the trust boundary.
- Native AOT supports explicit scalar, collection, map, delegate, and typed
  schema paths. Reflection-based schema discovery is rejected when dynamic code
  is unavailable so a trimmed binary cannot silently compile an incomplete
  host contract.

These host-integration restrictions do not change the expression language.
They are reviewed platform/security mappings around values supplied by the
embedding application.
