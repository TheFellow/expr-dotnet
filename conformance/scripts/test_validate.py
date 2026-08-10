#!/usr/bin/env python3
"""Unit tests for stdlib-only corpus validation."""

from __future__ import annotations

import copy
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from common import REPOSITORY, REVISION  # noqa: E402
from validate import validate_case, validate_outcome  # noqa: E402


class ValidationTests(unittest.TestCase):
    def test_accepts_recursive_normalized_map(self) -> None:
        validate_outcome(
            {
                "status": "success",
                "phase": "runtime",
                "type": "map",
                "value": {
                    "kind": "map",
                    "value": [
                        {
                            "key": {"kind": "string", "value": "key"},
                            "value": {"kind": "array", "value": [{"kind": "integer", "value": "1"}]},
                        }
                    ],
                },
            },
            "outcome",
        )

    def test_rejects_runtime_type_mismatch(self) -> None:
        with self.assertRaisesRegex(ValueError, "runtime type and value kind differ"):
            validate_outcome(
                {
                    "status": "success",
                    "phase": "runtime",
                    "type": "float",
                    "value": {"kind": "integer", "value": "1"},
                },
                "outcome",
            )

    def test_rejects_duplicate_case_identifier(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            upstream = Path(directory)
            source = upstream / "sample_test.go"
            source.write_text("func TestSample() {}\n", encoding="utf-8")
            case = {
                "__corpus_line": 1,
                "schema": "expr.conformance.case/v1",
                "id": "sample/case",
                "expression": "true",
                "expected": {
                    "status": "success",
                    "phase": "runtime",
                    "type": "boolean",
                    "value": {"kind": "boolean", "value": True},
                },
                "provenance": {
                    "repository": REPOSITORY,
                    "revision": REVISION,
                    "path": "sample_test.go",
                    "test": "TestSample",
                    "line": 1,
                },
            }
            seen: set[str] = set()
            validate_case(copy.deepcopy(case), upstream, seen)
            with self.assertRaisesRegex(ValueError, "duplicate id"):
                validate_case(copy.deepcopy(case), upstream, seen)

    def test_rejects_provenance_that_is_only_a_test_name_prefix(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            upstream = Path(directory)
            source = upstream / "sample_test.go"
            source.write_text("func TestSampleExtended() {}\n", encoding="utf-8")
            case = {
                "__corpus_line": 1,
                "schema": "expr.conformance.case/v1",
                "id": "sample/case",
                "expression": "true",
                "expected": {
                    "status": "success",
                    "phase": "runtime",
                    "type": "boolean",
                    "value": {"kind": "boolean", "value": True},
                },
                "provenance": {
                    "repository": REPOSITORY,
                    "revision": REVISION,
                    "path": "sample_test.go",
                    "test": "TestSample",
                    "line": 1,
                },
            }

            with self.assertRaisesRegex(ValueError, "top-level test"):
                validate_case(case, upstream, set())


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.discover(str(Path(__file__).resolve().parent), pattern="test_*.py")
    result = unittest.TextTestRunner().run(suite)
    raise SystemExit(0 if result.wasSuccessful() else 1)
