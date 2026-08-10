#!/usr/bin/env python3
"""Append-only stewardship ledger for the Expr semantic port."""

from __future__ import annotations

import argparse
import dataclasses
import datetime as dt
import os
import re
import subprocess
import sys
from collections.abc import Iterable, Sequence
from pathlib import Path, PurePosixPath


HEADER = "event\tsha\tupstream_iso8601\tdisposition\tevidence"
INITIAL = frozenset({"baseline", "pending"})
TERMINAL = frozenset({"implemented", "acknowledged", "wedged"})
DISPOSITIONS = INITIAL | TERMINAL
SHA_PATTERN = re.compile(r"[0-9a-f]{40}")


class LedgerError(ValueError):
    """Raised when the ledger violates its append-only state model."""


@dataclasses.dataclass(frozen=True, slots=True)
class Entry:
    """One immutable ledger event."""

    event: int
    sha: str
    upstream_iso8601: str
    disposition: str
    evidence: str = "-"

    @classmethod
    def parse(cls, line: str) -> Entry:
        """Parse one ledger row."""
        fields = line.rstrip("\n").split("\t")
        if len(fields) != 5:
            raise LedgerError(f"expected 5 TSV fields, found {len(fields)}: {line!r}")
        try:
            event = int(fields[0])
        except ValueError as error:
            raise LedgerError(f"invalid event number {fields[0]!r}") from error
        return cls(event, fields[1], fields[2], fields[3], fields[4])

    def serialize(self) -> str:
        """Serialize this event as one TSV row."""
        return (
            f"{self.event}\t{self.sha}\t{self.upstream_iso8601}\t"
            f"{self.disposition}\t{self.evidence}\n"
        )


class Ledger:
    """Validated view of an append-only ledger."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.entries: list[Entry] = []

    def load(self) -> Ledger:
        """Load and validate all existing events."""
        if not self.path.exists():
            return self
        lines = self.path.read_text(encoding="utf-8").splitlines()
        if not lines or lines[0] != HEADER:
            raise LedgerError(f"{self.path}: missing exact ledger header")
        self.entries = [Entry.parse(f"{line}\n") for line in lines[1:]]
        self.validate()
        return self

    def validate(self) -> None:
        """Validate event numbering, identities, transitions, and evidence paths."""
        states: dict[str, Entry] = {}
        first_seen: dict[str, int] = {}
        for expected_event, entry in enumerate(self.entries, start=1):
            if entry.event != expected_event:
                raise LedgerError(
                    f"event {entry.event} is out of sequence; expected {expected_event}"
                )
            if SHA_PATTERN.fullmatch(entry.sha) is None:
                raise LedgerError(f"event {entry.event} has a non-canonical full SHA")
            self._parse_timestamp(entry)
            if entry.disposition not in DISPOSITIONS:
                raise LedgerError(
                    f"event {entry.event} has unknown disposition {entry.disposition!r}"
                )
            self._validate_evidence(entry)

            prior = states.get(entry.sha)
            if prior is None:
                if entry.disposition not in INITIAL:
                    raise LedgerError(
                        f"first event for {entry.sha} must be baseline or pending"
                    )
                first_seen[entry.sha] = entry.event
            elif prior.disposition != "pending" or entry.disposition not in TERMINAL:
                raise LedgerError(
                    f"invalid transition for {entry.sha}: "
                    f"{prior.disposition} -> {entry.disposition}"
                )
            elif prior.upstream_iso8601 != entry.upstream_iso8601:
                raise LedgerError(f"upstream timestamp changed for {entry.sha}")
            states[entry.sha] = entry

        # A terminal event may only close the oldest pending commit. This makes
        # dependency ordering an invariant of the ledger, not an agent promise.
        pending: list[tuple[int, str]] = []
        state_at_event: dict[str, str] = {}
        for entry in self.entries:
            if entry.sha not in state_at_event and entry.disposition == "pending":
                pending.append((first_seen[entry.sha], entry.sha))
            elif entry.disposition in TERMINAL:
                open_pending = [item for item in pending if state_at_event.get(item[1]) != "closed"]
                if not open_pending or open_pending[0][1] != entry.sha:
                    raise LedgerError(
                        f"{entry.sha} was closed before an earlier pending commit"
                    )
                state_at_event[entry.sha] = "closed"

    @staticmethod
    def _parse_timestamp(entry: Entry) -> dt.datetime:
        try:
            value = dt.datetime.fromisoformat(entry.upstream_iso8601.replace("Z", "+00:00"))
        except ValueError as error:
            raise LedgerError(
                f"event {entry.event} has invalid ISO-8601 timestamp"
            ) from error
        if value.tzinfo is None:
            raise LedgerError(f"event {entry.event} timestamp must include an offset")
        return value

    @staticmethod
    def _validate_evidence(entry: Entry) -> None:
        expected = "-"
        if entry.disposition == "acknowledged":
            expected = f"semport/skipped/{entry.sha}.md"
        elif entry.disposition == "wedged":
            expected = f"semport/wedged/{entry.sha}.md"
        if entry.evidence != expected:
            raise LedgerError(
                f"event {entry.event} evidence must be {expected!r}, "
                f"found {entry.evidence!r}"
            )

    def states(self) -> dict[str, Entry]:
        """Return the latest event for every upstream commit."""
        return {entry.sha: entry for entry in self.entries}

    def next_pending(self) -> Entry | None:
        """Return the oldest still-pending commit in discovery order."""
        states = self.states()
        return next(
            (
                entry
                for entry in self.entries
                if entry.disposition == "pending"
                and states[entry.sha].disposition == "pending"
            ),
            None,
        )

    def append(self, entries: Iterable[Entry]) -> int:
        """Append validated events without rewriting any existing byte."""
        additions = list(entries)
        if not additions:
            return 0
        candidate = Ledger(self.path)
        candidate.entries = [*self.entries, *additions]
        candidate.validate()

        self.path.parent.mkdir(parents=True, exist_ok=True)
        new_file = not self.path.exists()
        with self.path.open("a", encoding="utf-8", newline="") as stream:
            if new_file:
                stream.write(f"{HEADER}\n")
            for entry in additions:
                stream.write(entry.serialize())
            stream.flush()
            os.fsync(stream.fileno())
        self.entries.extend(additions)
        return len(additions)

    def verify_breadcrumbs(self, repository: Path) -> None:
        """Require every terminal skip or wedge breadcrumb to exist."""
        for entry in self.states().values():
            if entry.evidence == "-":
                continue
            path = repository / PurePosixPath(entry.evidence)
            if not path.is_file():
                raise LedgerError(f"missing evidence for {entry.sha}: {entry.evidence}")
            contents = path.read_text(encoding="utf-8")
            if entry.sha not in contents:
                raise LedgerError(f"evidence does not name full SHA {entry.sha}: {path}")


def git_history(repository: Path, revision: str) -> list[tuple[str, str]]:
    """Read reachable commits in topology-safe oldest-first order."""
    command = [
        "git",
        "-C",
        str(repository),
        "log",
        "--reverse",
        "--topo-order",
        "--format=%H%x09%cI",
        revision,
    ]
    result = subprocess.run(command, check=True, capture_output=True, text=True)
    commits: list[tuple[str, str]] = []
    for line in result.stdout.splitlines():
        sha, timestamp = line.split("\t", maxsplit=1)
        commits.append((sha, timestamp))
    return commits


def make_entries(
    ledger: Ledger,
    commits: Sequence[tuple[str, str]],
    disposition: str,
) -> list[Entry]:
    """Create sequential events for commits not already represented."""
    known = ledger.states()
    next_event = len(ledger.entries) + 1
    entries: list[Entry] = []
    for sha, timestamp in commits:
        if sha in known:
            continue
        entries.append(Entry(next_event, sha, timestamp, disposition))
        next_event += 1
    return entries


def default_ledger_path() -> Path:
    """Locate the repository ledger independently of the current directory."""
    return Path(__file__).resolve().with_name("ledger.tsv")


def build_parser() -> argparse.ArgumentParser:
    """Build the command-line parser."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ledger", type=Path, default=default_ledger_path())
    subparsers = parser.add_subparsers(dest="command", required=True)

    baseline = subparsers.add_parser("baseline", help="initialize baseline events")
    baseline.add_argument("--upstream", type=Path, required=True)
    baseline.add_argument("--through", required=True)

    discover = subparsers.add_parser("discover", help="append unseen commits as pending")
    discover.add_argument("--upstream", type=Path, required=True)
    discover.add_argument("--revision", default="origin/master")

    subparsers.add_parser("next", help="print the next pending event")

    transition = subparsers.add_parser("transition", help="close the next pending event")
    transition.add_argument("sha")
    transition.add_argument("disposition", choices=sorted(TERMINAL))

    verify = subparsers.add_parser("verify", help="validate ledger and breadcrumbs")
    verify.add_argument("--repository", type=Path, default=Path.cwd())

    subparsers.add_parser("status", help="summarize current dispositions")
    return parser


def run(arguments: Sequence[str] | None = None) -> int:
    """Run the ledger command-line interface."""
    args = build_parser().parse_args(arguments)
    ledger = Ledger(args.ledger).load()

    if args.command == "baseline":
        if ledger.entries:
            raise LedgerError("baseline initialization requires an empty ledger")
        commits = git_history(args.upstream, args.through)
        count = ledger.append(make_entries(ledger, commits, "baseline"))
        print(f"appended {count} baseline events through {commits[-1][0]}")
    elif args.command == "discover":
        commits = git_history(args.upstream, args.revision)
        count = ledger.append(make_entries(ledger, commits, "pending"))
        print(f"appended {count} pending events")
    elif args.command == "next":
        entry = ledger.next_pending()
        if entry is None:
            print("CAUGHT_UP")
        else:
            print(entry.serialize(), end="")
    elif args.command == "transition":
        pending = ledger.next_pending()
        if pending is None:
            raise LedgerError("there is no pending commit")
        if args.sha != pending.sha:
            raise LedgerError(f"next pending commit is {pending.sha}, not {args.sha}")
        evidence = "-"
        if args.disposition == "acknowledged":
            evidence = f"semport/skipped/{pending.sha}.md"
        elif args.disposition == "wedged":
            evidence = f"semport/wedged/{pending.sha}.md"
        ledger.append(
            [
                Entry(
                    len(ledger.entries) + 1,
                    pending.sha,
                    pending.upstream_iso8601,
                    args.disposition,
                    evidence,
                )
            ]
        )
        print(f"transitioned {pending.sha} to {args.disposition}")
    elif args.command == "verify":
        ledger.verify_breadcrumbs(args.repository.resolve())
        print(f"verified {len(ledger.entries)} events for {len(ledger.states())} commits")
    elif args.command == "status":
        counts = {disposition: 0 for disposition in sorted(DISPOSITIONS)}
        for entry in ledger.states().values():
            counts[entry.disposition] += 1
        for disposition, count in counts.items():
            print(f"{disposition}: {count}")
        pending = ledger.next_pending()
        print(f"next: {pending.sha if pending else 'CAUGHT_UP'}")
    return 0


def main() -> int:
    """Translate validation errors into concise CLI failures."""
    try:
        return run()
    except (LedgerError, subprocess.CalledProcessError) as error:
        print(f"ledger: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
