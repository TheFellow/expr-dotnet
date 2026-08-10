#!/usr/bin/env python3
"""Validate corpus structure, provenance, and pinned-oracle outcomes."""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import re
import sys
from pathlib import Path
from typing import Any

from common import (
    CASE_SCHEMA,
    DEFAULT_BUILTIN_INVENTORY,
    DEFAULT_CORPUS,
    DEFAULT_UPSTREAM,
    REPOSITORY,
    RESULT_SCHEMA,
    REVISION,
    outcome,
    read_cases,
    run_oracle,
)

ALLOWED_CASE_KEYS = {
    "schema", "id", "expression", "operation", "environment", "options",
    "provenance", "expected", "__corpus_line",
}
ALLOWED_OPTIONS = {
    "allowUndefinedVariables", "optimize", "disableShortCircuit",
    "disableIfOperator", "disableAllBuiltins", "disableBuiltins",
    "enableBuiltins", "timezone", "maxNodes", "expectedType",
}
IDENTIFIER_PATTERN = re.compile(r"^[a-z0-9][a-z0-9./_-]*$")
INTEGER_PATTERN = re.compile(r"^-?[0-9]+$")
VALUE_KINDS = {"null", "boolean", "integer", "float", "string", "bytes", "array", "map", "time", "duration"}
PHASES = {"request", "oracle", "compile", "runtime", "normalize"}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def is_integer(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def validate_value(value: Any, label: str) -> None:
    require(isinstance(value, dict), f"{label}: normalized value must be an object")
    require(set(value) <= {"kind", "value"}, f"{label}: unknown normalized value fields")
    kind = value.get("kind")
    require(kind in VALUE_KINDS, f"{label}: invalid normalized kind {kind!r}")
    if kind == "null":
        require(set(value) == {"kind"}, f"{label}: null must not carry a value")
        return
    require(set(value) == {"kind", "value"}, f"{label}: {kind} requires a value")
    payload = value["value"]
    if kind == "boolean":
        require(isinstance(payload, bool), f"{label}: boolean payload must be bool")
    elif kind == "integer":
        require(isinstance(payload, str) and INTEGER_PATTERN.fullmatch(payload) is not None, f"{label}: invalid integer payload")
    elif kind == "float":
        require(isinstance(payload, str) and payload, f"{label}: invalid float payload")
    elif kind in {"string", "time"}:
        require(isinstance(payload, str), f"{label}: {kind} payload must be a string")
    elif kind == "duration":
        require(isinstance(payload, str) and INTEGER_PATTERN.fullmatch(payload) is not None, f"{label}: duration must be integer nanoseconds")
    elif kind == "bytes":
        require(isinstance(payload, str), f"{label}: bytes payload must be Base64 text")
        try:
            base64.b64decode(payload, validate=True)
        except (ValueError, binascii.Error) as error:
            raise ValueError(f"{label}: invalid Base64 payload") from error
    elif kind == "array":
        require(isinstance(payload, list), f"{label}: array payload must be a list")
        for index, item in enumerate(payload):
            validate_value(item, f"{label}[{index}]")
    elif kind == "map":
        require(isinstance(payload, list), f"{label}: map payload must be a list")
        keys: list[str] = []
        for index, entry in enumerate(payload):
            require(isinstance(entry, dict) and set(entry) == {"key", "value"}, f"{label}[{index}]: invalid map entry")
            validate_value(entry["key"], f"{label}[{index}].key")
            validate_value(entry["value"], f"{label}[{index}].value")
            keys.append(json.dumps(entry["key"], ensure_ascii=False, separators=(",", ":"), sort_keys=False))
        require(keys == sorted(keys), f"{label}: map keys are not canonically sorted")
        require(len(keys) == len(set(keys)), f"{label}: map contains duplicate normalized keys")


def validate_diagnostic(value: Any, label: str) -> None:
    require(isinstance(value, dict), f"{label}: diagnostic must be an object")
    require(set(value) <= {"message", "from", "to", "line", "column"}, f"{label}: unknown diagnostic fields")
    require(isinstance(value.get("message"), str), f"{label}: diagnostic message is required")
    for field in ("from", "to"):
        if field in value:
            require(is_integer(value[field]) and value[field] >= 0, f"{label}: invalid {field}")
    for field in ("line", "column"):
        if field in value:
            require(is_integer(value[field]) and value[field] >= 1, f"{label}: invalid {field}")
    require(("from" in value) == ("to" in value), f"{label}: from and to must appear together")
    require(("line" in value) == ("column" in value), f"{label}: line and column must appear together")
    if "from" in value:
        require(value["from"] <= value["to"], f"{label}: diagnostic span is reversed")


def validate_outcome(value: Any, label: str) -> None:
    require(isinstance(value, dict), f"{label}: outcome must be an object")
    require(set(value) <= {"status", "phase", "type", "value", "diagnostic"}, f"{label}: unknown outcome fields")
    status = value.get("status")
    phase = value.get("phase")
    require(status in {"success", "error"}, f"{label}: invalid status")
    require(phase in PHASES, f"{label}: invalid phase")
    if "type" in value:
        require(value["type"] in VALUE_KINDS | {"any"}, f"{label}: invalid semantic type")
    if status == "success":
        require("diagnostic" not in value, f"{label}: success cannot have a diagnostic")
        require("type" in value, f"{label}: success requires a semantic type")
        if phase == "compile":
            require("value" not in value, f"{label}: compile success cannot have a runtime value")
        else:
            require(phase == "runtime" and "value" in value, f"{label}: evaluation success requires a runtime value")
    else:
        require("diagnostic" in value, f"{label}: error requires a diagnostic")
        require("value" not in value, f"{label}: error cannot have a value")
    if "value" in value:
        validate_value(value["value"], f"{label}.value")
        require(value.get("type") == value["value"]["kind"], f"{label}: runtime type and value kind differ")
    if "diagnostic" in value:
        validate_diagnostic(value["diagnostic"], f"{label}.diagnostic")


def validate_case(case: dict[str, Any], upstream: Path, seen: set[str]) -> None:
    label = f"corpus line {case['__corpus_line']}"
    unknown = set(case) - ALLOWED_CASE_KEYS
    require(not unknown, f"{label}: unknown fields {sorted(unknown)}")
    require(case.get("schema") == CASE_SCHEMA, f"{label}: invalid schema")
    identifier = case.get("id")
    require(isinstance(identifier, str) and IDENTIFIER_PATTERN.fullmatch(identifier) is not None, f"{label}: invalid id")
    require(identifier not in seen, f"{label}: duplicate id {identifier!r}")
    seen.add(identifier)
    require(isinstance(case.get("expression"), str), f"{identifier}: expression must be a string")
    require(case.get("operation", "evaluate") in {"compile", "evaluate"}, f"{identifier}: invalid operation")
    options = case.get("options", {})
    require(isinstance(options, dict), f"{identifier}: options must be an object")
    require(not (set(options) - ALLOWED_OPTIONS), f"{identifier}: unknown options {sorted(set(options) - ALLOWED_OPTIONS)}")
    for name in ("allowUndefinedVariables", "optimize", "disableShortCircuit", "disableIfOperator", "disableAllBuiltins"):
        if name in options:
            require(isinstance(options[name], bool), f"{identifier}: option {name} must be boolean")
    for name in ("disableBuiltins", "enableBuiltins"):
        if name in options:
            require(isinstance(options[name], list) and all(isinstance(item, str) and item for item in options[name]), f"{identifier}: option {name} must contain names")
            require(len(options[name]) == len(set(options[name])), f"{identifier}: option {name} contains duplicates")
    if "timezone" in options:
        require(isinstance(options["timezone"], str) and options["timezone"], f"{identifier}: timezone must be nonempty")
    if "maxNodes" in options:
        require(is_integer(options["maxNodes"]) and options["maxNodes"] >= 0, f"{identifier}: maxNodes must be nonnegative")
    if "expectedType" in options:
        require(options["expectedType"] in {"any", "bool", "int", "int64", "float64"}, f"{identifier}: invalid expectedType")
    validate_outcome(case.get("expected"), f"{identifier}.expected")

    provenance = case.get("provenance")
    require(isinstance(provenance, dict), f"{identifier}: provenance is required")
    require(set(provenance) == {"repository", "revision", "path", "test", "line"}, f"{identifier}: invalid provenance fields")
    require(provenance.get("repository") == REPOSITORY, f"{identifier}: wrong provenance repository")
    require(provenance.get("revision") == REVISION, f"{identifier}: wrong provenance revision")
    relative = provenance.get("path")
    require(isinstance(relative, str) and relative and not Path(relative).is_absolute(), f"{identifier}: invalid provenance path")
    source = (upstream / relative).resolve()
    require(upstream.resolve() in source.parents, f"{identifier}: provenance escapes upstream root")
    require(source.is_file(), f"{identifier}: missing provenance source {relative}")
    line = provenance.get("line")
    require(is_integer(line) and line > 0, f"{identifier}: invalid provenance line")
    with source.open("r", encoding="utf-8") as stream:
        line_count = sum(1 for _ in stream)
    require(line <= line_count, f"{identifier}: provenance line {line} exceeds {relative} ({line_count})")
    test = provenance.get("test")
    require(isinstance(test, str) and test, f"{identifier}: provenance test is required")
    require(test in source.read_text(encoding="utf-8"), f"{identifier}: test {test!r} not found in {relative}")


def validate_builtin_inventory(path: Path, upstream: Path, cases: list[dict[str, Any]]) -> int:
    inventory = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(inventory, dict), "built-in inventory must be an object")
    require(set(inventory) == {"schema", "revision", "source", "count", "names"}, "invalid built-in inventory fields")
    require(inventory["schema"] == "expr.conformance.builtin-inventory/v1", "invalid built-in inventory schema")
    require(inventory["revision"] == REVISION, "built-in inventory revision is not pinned")
    names = inventory["names"]
    require(isinstance(names, list) and all(isinstance(name, str) and name for name in names), "invalid built-in names")
    require(inventory["count"] == len(names) and len(names) == len(set(names)), "built-in count or uniqueness mismatch")

    source_path = upstream / inventory["source"]
    require(source_path.is_file(), f"missing built-in source {source_path}")
    source = source_path.read_text(encoding="utf-8")
    source = source[source.index("var Builtins ="):]
    upstream_names = re.findall(r'(?:Name:\s*|bitFunc\()"([^"]+)"', source)
    require(names == upstream_names, "built-in inventory differs from pinned builtin.Builtins order")

    expressions = "\n".join(case["expression"] for case in cases)
    missing = [name for name in names if re.search(rf"(?<![A-Za-z0-9_]){re.escape(name)}\s*\(", expressions) is None]
    require(not missing, f"built-ins without an initial corpus call: {missing}")
    return len(names)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", type=Path, default=DEFAULT_CORPUS)
    parser.add_argument("--builtin-inventory", type=Path, default=DEFAULT_BUILTIN_INVENTORY)
    parser.add_argument("--upstream", type=Path, default=DEFAULT_UPSTREAM)
    parser.add_argument("--skip-oracle", action="store_true")
    args = parser.parse_args()

    try:
        cases = read_cases(args.corpus)
        require(cases, "corpus is empty")
        seen: set[str] = set()
        for case in cases:
            validate_case(case, args.upstream, seen)
        builtin_count = validate_builtin_inventory(args.builtin_inventory, args.upstream, cases)

        if not args.skip_oracle:
            results = run_oracle(cases)
            differences: list[str] = []
            for case, result in zip(cases, results, strict=True):
                if result.get("schema") != RESULT_SCHEMA:
                    differences.append(f"{case['id']}: wrong result schema")
                    continue
                if result.get("id") != case["id"] or result.get("upstreamRevision") != REVISION:
                    differences.append(f"{case['id']}: wrong result identity or revision")
                    continue
                actual = outcome(result)
                validate_outcome(actual, f"{case['id']}.actual")
                if actual != case["expected"]:
                    differences.append(
                        f"{case['id']}: expected {json.dumps(case['expected'], sort_keys=True)} "
                        f"but got {json.dumps(actual, sort_keys=True)}"
                    )
            require(not differences, "oracle differences:\n" + "\n".join(differences))
        mode = "structure/provenance" if args.skip_oracle else "structure/provenance/oracle"
        print(f"validated {len(cases)} cases and {builtin_count} built-ins ({mode}) at {REVISION}")
        return 0
    except (OSError, ValueError, RuntimeError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
