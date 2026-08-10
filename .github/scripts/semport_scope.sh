#!/usr/bin/env bash
set -euo pipefail

status=0
while IFS= read -r path; do
  case "$path" in
    src/*|tests/*|benchmarks/*|docs/*|README.md|SECURITY.md|NOTICE|Directory.Build.props|Directory.Build.targets|Directory.Packages.props|expr-dotnet.slnx)
      ;;
    semport/ledger.tsv|semport/skipped/*|semport/wedged/*)
      echo "semport_scope: implementation stage changed stewardship-owned path: $path" >&2
      status=1
      ;;
    .ai/*|inspiration/expr/*)
      ;;
    *)
      echo "semport_scope: unexpected implementation path: $path" >&2
      status=1
      ;;
  esac
done < <(git status --short | sed -E 's/^...//')

exit "$status"
