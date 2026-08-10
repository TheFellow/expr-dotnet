#!/usr/bin/env python3
"""Shared stdlib-only helpers for the Expr differential corpus."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any

REVISION = "4b31df3a2e0eefec04c017a82a00e0f08541d3e4"
CASE_SCHEMA = "expr.conformance.case/v1"
RESULT_SCHEMA = "expr.conformance.result/v1"
REPOSITORY = "https://github.com/expr-lang/expr"
ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CORPUS = ROOT / "conformance" / "corpus" / "upstream.jsonl"
DEFAULT_BUILTIN_INVENTORY = ROOT / "conformance" / "inventory" / "builtins.json"
DEFAULT_UPSTREAM = ROOT / "inspiration" / "expr"
ORACLE_DIRECTORY = ROOT / "tools" / "Expr.Oracle"


def read_cases(path: Path) -> list[dict[str, Any]]:
    cases: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            try:
                value = json.loads(line)
            except json.JSONDecodeError as error:
                raise ValueError(f"{path}:{line_number}: {error}") from error
            if not isinstance(value, dict):
                raise ValueError(f"{path}:{line_number}: case must be an object")
            value["__corpus_line"] = line_number
            cases.append(value)
    return cases


def wire_case(case: dict[str, Any]) -> dict[str, Any]:
    return {key: value for key, value in case.items() if not key.startswith("__")}


def run_oracle(cases: list[dict[str, Any]]) -> list[dict[str, Any]]:
    payload = "".join(
        json.dumps(wire_case(case), ensure_ascii=False, separators=(",", ":")) + "\n"
        for case in cases
    )
    completed = subprocess.run(
        ["go", "run", "."],
        cwd=ORACLE_DIRECTORY,
        input=payload,
        text=True,
        capture_output=True,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(f"oracle failed ({completed.returncode}): {completed.stderr.strip()}")
    results = [json.loads(line) for line in completed.stdout.splitlines() if line.strip()]
    if len(results) != len(cases):
        raise RuntimeError(f"oracle returned {len(results)} results for {len(cases)} cases")
    return results


def outcome(result: dict[str, Any]) -> dict[str, Any]:
    return {
        key: value
        for key, value in result.items()
        if key not in {"schema", "id", "upstreamRevision"}
    }


def encode_case(case: dict[str, Any]) -> str:
    return json.dumps(wire_case(case), ensure_ascii=False, separators=(",", ":"))
