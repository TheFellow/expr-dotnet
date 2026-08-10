# Feature Parity Contract

Expr.NET targets semantic parity with the Go revision recorded in
[`upstream.md`](upstream.md). The Go implementation and its tests are the oracle
until the Expr project publishes a language-independent conformance suite.

Parity means that equivalent source text, configuration, and environment values
produce equivalent compile outcomes, result values, observable result types, and
diagnostics. Equivalent does not require identical implementation structure or
public API spelling: the .NET API should remain idiomatic C#.

## Pinned upstream inventory

The pinned revision contains:

- 585 top-level Go tests, 42 executable examples, and approximately 15,500
  lines of non-vendored tests
- 59 benchmarks and one fuzz harness
- 23 public AST node kinds
- 71 standard built-ins
- 84 virtual-machine opcodes, including the invalid and terminal sentinels

These counts are an inventory aid, not a substitute for behavioral coverage.
The semport ledger is the source of truth for commits examined after the pin.
The symbol-level [upstream traceability inventory](upstream-test-traceability.md)
records evidence for all 687 upstream entry points with zero explicit gaps.

## Required surfaces

| Surface | Required compatibility evidence |
| --- | --- |
| Source and diagnostics | UTF-8-aware locations, excerpts, stable error spans, lexer and parser failure corpus |
| Language syntax | Every literal, operator, precedence rule, optional chain, slice, pipe, range, `let`, predicate, and multiline conditional |
| Public syntax API | Immutable nodes, source locations, post-order walk, replacement that retains location, printing and discovery helpers |
| Static checking | Strict and undefined-variable modes, host/member discovery, collection/member/index rules, overload resolution, expected result types |
| Configuration | Environment, custom functions, patches, operator overloads, constants, built-in controls, context, timezone, node budget, optimizer and short-circuit controls |
| Built-ins | All upstream names, arities, accepted types, return types, edge cases, predicate scopes, and error behavior |
| Optimization | Equivalent results with optimization on and off, including every upstream AST optimization family |
| Compilation | Stable immutable program representation, source-to-instruction locations, variable slots, constants, function bindings, and debug metadata |
| Evaluation | Every opcode, short circuit behavior, runtime conversions, indexing/slicing, host calls, predicates, errors, and resource budgets |
| Tooling | Go differential oracle, extracted corpus, fuzz/property tests, benchmarks, security corpus, package and Native AOT smoke tests |

## Language checklist

- Literals: nil, Boolean, decimal/hex/octal/binary integer, float, escaped and
  raw string, bytes, array, and map.
- Arithmetic: `+`, `-`, `*`, `/`, `%`, `^`, and `**`.
- Comparison and logic: equality, ordering, `not`/`!`, `and`/`&&`, and
  `or`/`||`.
- Control flow: ternary, nil coalescing, and `if { } else { }`.
- Access: dot, bracket, optional chaining, negative indices, slices, and
  membership.
- Other operators: string relations, regular-expression match, inclusive
  range, and pipe.
- Bindings and scopes: `let`, `$env`, predicates, `#`, `#index`, and `#acc`.

## Built-in checklist

Predicate built-ins:

`all`, `none`, `any`, `one`, `filter`, `map`, `find`, `findIndex`,
`findLast`, `findLastIndex`, `count`, `sum`, `groupBy`, `sortBy`, and `reduce`.

Value built-ins:

`len`, `type`, `abs`, `ceil`, `floor`, `round`, `int`, `float`, `string`,
`trim`, `trimPrefix`, `trimSuffix`, `upper`, `lower`, `split`, `splitAfter`,
`replace`, `repeat`, `join`, `indexOf`, `lastIndexOf`, `hasPrefix`,
`hasSuffix`, `max`, `min`, `mean`, `median`, `toJSON`, `fromJSON`,
`toBase64`, `fromBase64`, `now`, `duration`, `date`, `timezone`, `first`,
`last`, `get`, `take`, `keys`, `values`, `toPairs`, `fromPairs`, `reverse`,
`uniq`, `concat`, `flatten`, `sort`, `bitand`, `bitor`, `bitxor`, `bitnand`,
`bitshl`, `bitshr`, `bitushr`, and `bitnot`.

## .NET host mapping

| Go concept | Expr.NET representation |
| --- | --- |
| `nil` | `null` |
| `bool` | `bool` |
| Expr integer | `long` internally; checked host conversions at API boundaries |
| Expr float | `double` |
| `string` | `string`, with Unicode behavior tested against Go code points |
| `[]byte` | `byte[]` or `ReadOnlyMemory<byte>` at public boundaries |
| array/slice | Arrays and `IReadOnlyList<T>`; adapters cached per host type |
| map | `IReadOnlyDictionary<TKey,TValue>` and dictionary adapters |
| `time.Time` | `DateTimeOffset`; timezone configuration uses `TimeZoneInfo` |
| `time.Duration` | `TimeSpan` |
| `context.Context` | `CancellationToken` injection for compatible custom functions |
| struct fields/tags | Public properties/fields, with an explicit Expr rename attribute |
| functions/methods | Delegates and public instance methods through cached descriptors |

Where the platforms cannot be made observably identical, the narrow difference
must be documented in [`compatibility.md`](compatibility.md) and protected by a
test before it is accepted.

## Completion gates

Feature parity may be claimed only when all of the following are true:

1. Every upstream test is linked to a passing .NET test, a differential corpus
   case, or a reviewed platform-difference entry.
2. Every built-in and opcode has direct tests, including failures and boundary
   values.
3. The differential suite reports no unexplained differences at the pinned
   revision.
4. Optimization-on and optimization-off results agree across the corpus.
5. Parser fuzzing and generated-expression properties pass their configured
   budgets.
6. Resource exhaustion, regex, reflection, serialization, and hostile host
   object cases pass the security corpus.
7. Release build, formatting, analyzers, tests, package validation, benchmarks,
   and the Native AOT smoke application pass on supported platforms.
8. The Attractor pipeline validates and every upstream commit through the
   current target has a terminal ledger disposition.

The first gate is enforced structurally by
`conformance/scripts/validate_traceability.py`. Its `--require-no-gaps` mode is
the release-parity gate; ordinary validation accepts, counts, and reports gaps
so incomplete coverage cannot be hidden behind a green inventory check.
