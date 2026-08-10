#!/usr/bin/env python3
"""Regenerate normalized expected outcomes using the pinned Go oracle."""

from __future__ import annotations

import argparse
import os
import sys
import tempfile
from pathlib import Path

from common import DEFAULT_CORPUS, encode_case, outcome, read_cases, run_oracle


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", type=Path, default=DEFAULT_CORPUS)
    parser.add_argument("--write", action="store_true", help="atomically replace the input corpus")
    args = parser.parse_args()

    try:
        cases = read_cases(args.corpus)
        results = run_oracle(cases)
        for case, result in zip(cases, results, strict=True):
            case["expected"] = outcome(result)
        rendered = "".join(encode_case(case) + "\n" for case in cases)
        if not args.write:
            sys.stdout.write(rendered)
            return 0

        args.corpus.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary_name = tempfile.mkstemp(prefix=".upstream.", suffix=".jsonl", dir=args.corpus.parent)
        try:
            with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
                stream.write(rendered)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary_name, args.corpus)
        except BaseException:
            Path(temporary_name).unlink(missing_ok=True)
            raise
        print(f"updated {len(cases)} cases in {args.corpus}", file=sys.stderr)
        return 0
    except (OSError, ValueError, RuntimeError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
