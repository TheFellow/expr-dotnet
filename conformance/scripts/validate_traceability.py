#!/usr/bin/env python3
"""Validate exact upstream test-symbol traceability and local evidence links."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any

from common import DEFAULT_CORPUS, DEFAULT_UPSTREAM, ROOT, read_cases
from traceability import (
    DEFAULT_TRACEABILITY,
    SUPPORT_PREFIXES,
    TRACEABILITY_SCHEMA,
    build_inventory,
)

DISPOSITIONS = {
    "differential_corpus",
    "dotnet_benchmark",
    "dotnet_test",
    "excluded_support",
    "gap",
    "platform_mapping",
}
EVIDENCE_TYPES = {
    "corpus",
    "documentation",
    "dotnet_benchmark",
    "dotnet_test",
    "fuzz_harness",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def resolve_local(root: Path, relative: str, label: str) -> Path:
    require(isinstance(relative, str) and relative and not Path(relative).is_absolute(), f"{label}: invalid path")
    path = (root / relative).resolve()
    require(path == root.resolve() or root.resolve() in path.parents, f"{label}: path escapes repository")
    require(path.is_file(), f"{label}: missing evidence path {relative}")
    return path


def validate_evidence(
    row: dict[str, Any],
    evidence: dict[str, Any],
    root: Path,
    cases_by_id: dict[str, dict[str, Any]],
) -> None:
    label = f"{row['path']}:{row['symbol']}"
    require(isinstance(evidence, dict), f"{label}: evidence must be an object")
    evidence_type = evidence.get("type")
    require(evidence_type in EVIDENCE_TYPES, f"{label}: invalid evidence type {evidence_type!r}")
    if evidence_type == "corpus":
        require(set(evidence) == {"type", "ids"}, f"{label}: invalid corpus evidence fields")
        identifiers = evidence["ids"]
        require(isinstance(identifiers, list) and identifiers, f"{label}: corpus evidence requires case ids")
        require(len(identifiers) == len(set(identifiers)), f"{label}: duplicate corpus evidence id")
        for identifier in identifiers:
            require(identifier in cases_by_id, f"{label}: unknown corpus case {identifier!r}")
            provenance = cases_by_id[identifier]["provenance"]
            require(
                (provenance["path"], provenance["test"]) == (row["path"], row["symbol"]),
                f"{label}: corpus case {identifier!r} points at another upstream symbol",
            )
        return

    expected_fields = {"type", "path"}
    if evidence_type == "documentation":
        expected_fields.add("anchor")
    elif evidence_type in {"dotnet_benchmark", "dotnet_test"} and "symbol" in evidence:
        expected_fields.add("symbol")
    require(set(evidence) == expected_fields, f"{label}: invalid {evidence_type} evidence fields")
    source = resolve_local(root, evidence["path"], label)
    text = source.read_text(encoding="utf-8")
    if evidence_type == "documentation":
        anchor = evidence["anchor"]
        require(isinstance(anchor, str) and anchor in text, f"{label}: documentation anchor {anchor!r} not found")
    elif "symbol" in evidence:
        symbol = evidence["symbol"]
        require(isinstance(symbol, str) and symbol, f"{label}: invalid local symbol")
        require(re.search(rf"\b{re.escape(symbol)}\s*\(", text) is not None, f"{label}: local symbol {symbol!r} not found")


def validate_traceability(
    inventory_path: Path = DEFAULT_TRACEABILITY,
    upstream: Path = DEFAULT_UPSTREAM,
    corpus: Path = DEFAULT_CORPUS,
    root: Path = ROOT,
) -> dict[str, Any]:
    inventory = json.loads(inventory_path.read_text(encoding="utf-8"))
    require(isinstance(inventory, dict), "traceability inventory must be an object")
    require(
        set(inventory) == {
            "schema",
            "revision",
            "sourceGlob",
            "sourceFileCount",
            "filesWithoutSymbols",
            "symbolCount",
            "dispositionCounts",
            "symbols",
        },
        "invalid traceability inventory fields",
    )
    require(inventory.get("schema") == TRACEABILITY_SCHEMA, "invalid traceability inventory schema")
    expected = build_inventory(upstream, corpus)
    require(
        inventory == expected,
        "traceability inventory is stale; run conformance/scripts/refresh_traceability.py --write",
    )

    cases = read_cases(corpus)
    cases_by_id = {case["id"]: case for case in cases}
    rows = inventory["symbols"]
    identities: set[tuple[str, str]] = set()
    for row in rows:
        label = f"{row.get('path')}:{row.get('symbol')}"
        require(
            set(row) == {"path", "symbol", "kind", "line", "disposition", "granularity", "evidence", "note"},
            f"{label}: invalid row fields",
        )
        identity = (row["path"], row["symbol"])
        require(identity not in identities, f"{label}: duplicate upstream symbol")
        identities.add(identity)
        require(row["disposition"] in DISPOSITIONS, f"{label}: invalid disposition")
        require(row["granularity"] in {"symbol", "file_family"}, f"{label}: invalid granularity")
        require(isinstance(row["line"], int) and row["line"] > 0, f"{label}: invalid source line")
        require(isinstance(row["note"], str) and row["note"], f"{label}: a nonempty note is required")
        require(isinstance(row["evidence"], list), f"{label}: evidence must be a list")
        if row["disposition"] in {"gap", "excluded_support"}:
            require(not row["evidence"], f"{label}: {row['disposition']} must not imply coverage evidence")
        else:
            require(row["evidence"], f"{label}: covered disposition requires evidence")
        if row["disposition"] == "excluded_support":
            require(row["path"].startswith(SUPPORT_PREFIXES), f"{label}: only embedded support packages may be excluded")
        for evidence in row["evidence"]:
            validate_evidence(row, evidence, root, cases_by_id)

    dispositions = Counter(row["disposition"] for row in rows)
    kinds = Counter(row["kind"] for row in rows)
    require(dict(sorted(dispositions.items())) == inventory["dispositionCounts"], "disposition summary is stale")
    return {
        "symbols": len(rows),
        "sourceFiles": inventory["sourceFileCount"],
        "symbolFiles": len({row["path"] for row in rows}),
        "filesWithoutSymbols": inventory["filesWithoutSymbols"],
        "kinds": dict(sorted(kinds.items())),
        "dispositions": dict(sorted(dispositions.items())),
        "gaps": dispositions["gap"],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--inventory", type=Path, default=DEFAULT_TRACEABILITY)
    parser.add_argument("--upstream", type=Path, default=DEFAULT_UPSTREAM)
    parser.add_argument("--corpus", type=Path, default=DEFAULT_CORPUS)
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--require-no-gaps", action="store_true")
    args = parser.parse_args()
    try:
        summary = validate_traceability(args.inventory, args.upstream, args.corpus)
        if args.require_no_gaps and summary["gaps"]:
            raise ValueError(f"traceability inventory contains {summary['gaps']} explicit gaps")
        if args.json:
            print(json.dumps(summary, sort_keys=True, separators=(",", ":")))
        else:
            print(
                f"validated {summary['symbols']} upstream symbols across {summary['sourceFiles']} test files; "
                f"{summary['gaps']} explicit gaps remain"
            )
        return 0
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
