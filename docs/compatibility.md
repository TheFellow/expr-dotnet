# Compatibility

The compatibility target is the upstream revision recorded in
[`upstream.md`](upstream.md). The Go implementation is the semantic oracle until
Expr publishes a language-independent conformance specification.

Platform differences must be narrow, explicit, and tested. This document will
record differences in host reflection, integer widths, time values, regular
expressions, Unicode handling, and native collection types as each area lands.

No intentional semantic differences are currently accepted.

