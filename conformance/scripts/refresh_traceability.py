#!/usr/bin/env python3
"""Regenerate the pinned upstream test traceability inventory."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from common import DEFAULT_CORPUS, DEFAULT_UPSTREAM
from traceability import DEFAULT_TRACEABILITY, build_inventory, encode_inventory


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--upstream", type=Path, default=DEFAULT_UPSTREAM)
    parser.add_argument("--corpus", type=Path, default=DEFAULT_CORPUS)
    parser.add_argument("--output", type=Path, default=DEFAULT_TRACEABILITY)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()

    encoded = encode_inventory(build_inventory(args.upstream, args.corpus))
    if args.write:
        temporary = args.output.with_suffix(args.output.suffix + ".tmp")
        temporary.write_text(encoded, encoding="utf-8", newline="\n")
        temporary.replace(args.output)
    else:
        sys.stdout.write(encoded)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
