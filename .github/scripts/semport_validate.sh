#!/usr/bin/env bash
set -uo pipefail

report=".ai/semport_validation.md"
details=".ai/semport_validation.details"
mkdir -p .ai
: >"$details"

status=0
run() {
  printf '\n$ %q' "$1" >>"$details"
  shift
  printf ' %q' "$@" >>"$details"
  printf '\n' >>"$details"
  "$@" >>"$details" 2>&1 || status=1
}

run restore dotnet restore expr-dotnet.slnx
run format dotnet format expr-dotnet.slnx --verify-no-changes --no-restore
run build dotnet build expr-dotnet.slnx --configuration Release --no-restore
run test dotnet test expr-dotnet.slnx --configuration Release --no-build
run pack dotnet pack src/Expr/Expr.csproj --configuration Release --no-build --output artifacts/packages

if [[ "$status" -eq 0 ]]; then
  printf 'PASS\n\n' >"$report"
else
  printf 'FAIL\n\n' >"$report"
fi
cat "$details" >>"$report"
rm -f "$details"
exit "$status"
