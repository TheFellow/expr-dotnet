#!/usr/bin/env python3
"""Unit tests for upstream test-symbol extraction and traceability classification."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from common import REPOSITORY, REVISION  # noqa: E402
from traceability import build_inventory, extract_symbols  # noqa: E402


class TraceabilityTests(unittest.TestCase):
    def test_extracts_top_level_go_test_symbols_and_ignores_receiver_methods(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            upstream = Path(directory)
            source = upstream / "sample_test.go"
            source.write_text(
                "func TestBehavior(t *testing.T) {}\n"
                "func BenchmarkBehavior(b *testing.B) {}\n"
                "func FuzzBehavior(f *testing.F) {}\n"
                "func ExampleBehavior() {}\n"
                "func (fixture *Fixture) TestMethod() {}\n",
                encoding="utf-8",
            )

            rows = extract_symbols(upstream)

            self.assertEqual(
                ["TestBehavior", "BenchmarkBehavior", "FuzzBehavior", "ExampleBehavior"],
                [row["symbol"] for row in rows],
            )

    def test_unmapped_symbol_is_an_explicit_gap(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            upstream = root / "upstream"
            upstream.mkdir()
            (upstream / "sample_test.go").write_text("func TestMissing(t *testing.T) {}\n", encoding="utf-8")
            corpus = root / "corpus.jsonl"
            corpus.write_text("", encoding="utf-8")

            inventory = build_inventory(upstream, corpus)

            self.assertEqual("gap", inventory["symbols"][0]["disposition"])

    def test_exact_corpus_provenance_takes_differential_disposition(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            upstream = root / "upstream"
            upstream.mkdir()
            (upstream / "sample_test.go").write_text("func TestCovered(t *testing.T) {}\n", encoding="utf-8")
            corpus = root / "corpus.jsonl"
            case = {
                "schema": "expr.conformance.case/v1",
                "id": "sample/covered",
                "expression": "true",
                "provenance": {
                    "repository": REPOSITORY,
                    "revision": REVISION,
                    "path": "sample_test.go",
                    "test": "TestCovered",
                    "line": 1,
                },
                "expected": {
                    "status": "success",
                    "phase": "runtime",
                    "type": "boolean",
                    "value": {"kind": "boolean", "value": True},
                },
            }
            corpus.write_text(json.dumps(case) + "\n", encoding="utf-8")

            inventory = build_inventory(upstream, corpus)

            row = inventory["symbols"][0]
            self.assertEqual("differential_corpus", row["disposition"])
            self.assertEqual(["sample/covered"], row["evidence"][0]["ids"])


if __name__ == "__main__":
    unittest.main()
