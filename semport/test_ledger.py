#!/usr/bin/env python3
"""Tests for the append-only semport ledger."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from ledger import Entry, Ledger, LedgerError, make_entries


SHA_A = "a" * 40
SHA_B = "b" * 40
TIME_A = "2026-07-01T12:00:00+00:00"
TIME_B = "2026-07-02T12:00:00+00:00"


class LedgerTests(unittest.TestCase):
    """Exercise persistence and state-machine invariants."""

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.path = self.root / "semport" / "ledger.tsv"

    def test_append_never_rewrites_existing_bytes(self) -> None:
        ledger = Ledger(self.path)
        ledger.append([Entry(1, SHA_A, TIME_A, "pending")])
        prefix = self.path.read_bytes()

        ledger.append([Entry(2, SHA_A, TIME_A, "implemented")])

        self.assertTrue(self.path.read_bytes().startswith(prefix))
        loaded = Ledger(self.path).load()
        self.assertEqual("implemented", loaded.states()[SHA_A].disposition)

    def test_only_oldest_pending_commit_can_close(self) -> None:
        ledger = Ledger(self.path)
        ledger.append(
            [
                Entry(1, SHA_A, TIME_A, "pending"),
                Entry(2, SHA_B, TIME_B, "pending"),
            ]
        )

        with self.assertRaisesRegex(LedgerError, "before an earlier pending"):
            ledger.append([Entry(3, SHA_B, TIME_B, "implemented")])

    def test_terminal_state_cannot_be_reopened_or_changed(self) -> None:
        ledger = Ledger(self.path)
        ledger.append(
            [
                Entry(1, SHA_A, TIME_A, "pending"),
                Entry(2, SHA_A, TIME_A, "implemented"),
            ]
        )

        with self.assertRaisesRegex(LedgerError, "invalid transition"):
            ledger.append([Entry(3, SHA_A, TIME_A, "wedged", f"semport/wedged/{SHA_A}.md")])

    def test_skip_requires_canonical_breadcrumb(self) -> None:
        ledger = Ledger(self.path)

        with self.assertRaisesRegex(LedgerError, "evidence must be"):
            ledger.append(
                [
                    Entry(1, SHA_A, TIME_A, "pending"),
                    Entry(2, SHA_A, TIME_A, "acknowledged", "notes/skip.md"),
                ]
            )

    def test_verify_requires_breadcrumb_to_name_full_sha(self) -> None:
        evidence = f"semport/skipped/{SHA_A}.md"
        ledger = Ledger(self.path)
        ledger.append(
            [
                Entry(1, SHA_A, TIME_A, "pending"),
                Entry(2, SHA_A, TIME_A, "acknowledged", evidence),
            ]
        )
        path = self.root / evidence
        path.parent.mkdir(parents=True)
        path.write_text("# unrelated\n", encoding="utf-8")

        with self.assertRaisesRegex(LedgerError, "does not name full SHA"):
            ledger.verify_breadcrumbs(self.root)

        path.write_text(f"# Skipped {SHA_A}\n", encoding="utf-8")
        ledger.verify_breadcrumbs(self.root)

    def test_discovery_preserves_upstream_order_and_ignores_known_sha(self) -> None:
        ledger = Ledger(self.path)
        ledger.append([Entry(1, SHA_A, TIME_A, "baseline")])

        additions = make_entries(
            ledger,
            [(SHA_A, TIME_A), (SHA_B, TIME_B)],
            "pending",
        )

        self.assertEqual([Entry(2, SHA_B, TIME_B, "pending")], additions)

    def test_rejects_abbreviated_sha_and_naive_timestamp(self) -> None:
        with self.assertRaisesRegex(LedgerError, "full SHA"):
            Ledger(self.path).append([Entry(1, "4b31df3", TIME_A, "baseline")])

        with self.assertRaisesRegex(LedgerError, "include an offset"):
            Ledger(self.path).append(
                [Entry(1, SHA_A, "2026-07-01T12:00:00", "baseline")]
            )


if __name__ == "__main__":
    unittest.main()
