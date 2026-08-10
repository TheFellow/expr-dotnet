# Expr.NET semantic-port stewardship

This directory tracks [`expr-lang/expr`](https://github.com/expr-lang/expr) as
an upstream semantic contract. The goal is feature parity expressed as
idiomatic C#—not line-for-line Go translation.

The initial baseline is the complete reachable history through
`4b31df3a2e0eefec04c017a82a00e0f08541d3e4` (1,028 commits). Every reachable
commit is represented by a `baseline` event in `ledger.tsv`. Later commits are
discovered and processed one at a time in topology-safe, oldest-first order.

## Ledger model

`ledger.tsv` is an append-only event log. Existing bytes are never edited,
sorted, or deleted. Its columns are:

| Column | Meaning |
| --- | --- |
| `event` | Contiguous local event sequence. |
| `sha` | Canonical 40-character upstream Git object ID. |
| `upstream_iso8601` | Upstream committer timestamp, including UTC offset. |
| `disposition` | `baseline`, `pending`, `implemented`, `acknowledged`, or `wedged`. |
| `evidence` | `-`, or the canonical skip/wedge breadcrumb path. |

The only legal transition is `pending` to one terminal disposition:

- `implemented`: a faithful C# change passed formatting, build, tests, package,
  semantic review, and commit gates.
- `acknowledged`: independent review found no port required. A detailed
  `semport/skipped/<full-sha>.md` breadcrumb is mandatory.
- `wedged`: a port attempt could not be made green. A detailed
  `semport/wedged/<full-sha>.md` breadcrumb and recoverable Git stash are
  mandatory. This is visible compatibility debt, not a claim of parity.

The tool rejects closing any commit other than the oldest pending commit. This
preserves semantic dependency order even when timestamps tie or Git history
contains merges. Full SHAs avoid the collision ambiguity of abbreviated IDs.

## Ledger commands

The ignored upstream checkout lives at `inspiration/expr`:

```sh
git clone https://github.com/expr-lang/expr.git inspiration/expr
git -C inspiration/expr fetch origin master
```

Common commands:

```sh
# Append every previously unseen reachable commit as pending.
python3 semport/ledger.py discover \
  --upstream inspiration/expr \
  --revision origin/master

# Print the one commit the pipeline may process, or CAUGHT_UP.
python3 semport/ledger.py next

# Append a terminal event; only the current `next` SHA is accepted.
python3 semport/ledger.py transition <full-sha> implemented
python3 semport/ledger.py transition <full-sha> acknowledged
python3 semport/ledger.py transition <full-sha> wedged

# Validate all state transitions and breadcrumb files.
python3 semport/ledger.py verify --repository .
python3 semport/ledger.py status
```

`baseline` is a one-time bootstrap command and refuses a non-empty ledger.

## Attractor pipeline

[`semport.dot`](semport.dot) is written for Attractor 0.19. It performs this
bounded workflow:

1. Require a clean worktree, refresh the ignored upstream clone, append newly
   discovered commits, and select exactly one oldest pending SHA.
2. Analyze that upstream diff and its tests as either `PORT` or `SKIP`.
3. Independently review skips before appending an `acknowledged` event.
4. For ports, produce a concrete C# plan, implement it, and enforce the
   implementation path boundary.
5. Run restore, `dotnet format`, Release build, all tests, and package creation.
6. Review semantic faithfulness, public walkable/patchable AST behavior,
   C# ergonomics, diagnostics, security, tests, and relevant benchmarks.
7. Append the terminal event only after approval, verify the ledger, and create
   one local commit corresponding to this one upstream commit.
8. On a failed retry, preserve work in a named Git stash, append a `wedged`
   event with evidence, and commit only the breadcrumb and ledger event.

The pipeline never pushes. Inspect its local commits and push them through the
normal repository workflow. Run it with:

```sh
/Users/ryan/bin/attractor --validate semport/semport.dot
/Users/ryan/bin/attractor semport/semport.dot
```

Scratch files go under ignored `.ai/`; the clone stays under ignored
`inspiration/expr/`.

## Local and CI integrity checks

```sh
.github/scripts/test_semport.sh
.github/scripts/test_semport.sh --base origin/main
```

The first command runs the stdlib-only Python tests and validates evidence. The
second additionally proves the current ledger begins with the exact bytes from
the base revision. `.github/workflows/semport-integrity.yml` runs the same guard
for semport changes without coupling it to the .NET CI workflow.

## Intentional differences from cedar-dotnet

This design retains cedar-dotnet's chronological one-commit loop, explicit
skip/wedge evidence, separate implementation and review agents, and green-build
commit gate. It deliberately strengthens several mechanics:

- transitions append events instead of mutating and re-sorting existing rows;
- full SHAs replace seven-character abbreviations;
- Git topological order replaces timestamp sorting;
- the ledger itself enforces that only the oldest pending commit can close;
- wedge changes are preserved in a recoverable stash instead of being erased
  with `git reset --hard` and `git clean`;
- discovered commits are checkpointed before implementation begins;
- every port runs the repository's complete format/build/test/pack contract;
- the pipeline creates local commits but does not autonomously push them; and
- integrity tests and a dedicated CI guard exercise ledger invariants and
  byte-for-byte append-only history.
