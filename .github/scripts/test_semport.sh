#!/usr/bin/env bash
set -euo pipefail

export PYTHONDONTWRITEBYTECODE=1

python3 -m unittest discover -s semport -p 'test_*.py' -v
python3 semport/ledger.py verify --repository .
if [[ "$#" -eq 0 ]]; then
  python3 .github/scripts/semport_guard.py
else
  python3 .github/scripts/semport_guard.py "$@"
fi
