#!/usr/bin/env python3
"""CI guard for append-only semport history and required evidence."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


REPOSITORY = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPOSITORY / "semport"))

from ledger import Ledger, LedgerError  # noqa: E402


def git_show(revision: str, path: str) -> bytes | None:
    """Read a repository file at a revision, or None when it did not exist."""
    result = subprocess.run(
        ["git", "show", f"{revision}:{path}"],
        cwd=REPOSITORY,
        capture_output=True,
        check=False,
    )
    if result.returncode == 0:
        return result.stdout
    return None


def main() -> int:
    """Validate current state and prove the ledger only grew from base."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", help="base git revision for append-only comparison")
    args = parser.parse_args()

    path = REPOSITORY / "semport" / "ledger.tsv"
    try:
        ledger = Ledger(path).load()
        ledger.verify_breadcrumbs(REPOSITORY)
    except LedgerError as error:
        print(f"semport_guard: {error}", file=sys.stderr)
        return 1

    if args.base:
        old = git_show(args.base, "semport/ledger.tsv")
        current = path.read_bytes()
        if old is not None and not current.startswith(old):
            print(
                "semport_guard: ledger.tsv was edited or truncated; "
                "only byte-for-byte appends are allowed",
                file=sys.stderr,
            )
            return 1

    print(
        f"semport_guard: verified {len(ledger.entries)} events for "
        f"{len(ledger.states())} commits"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
