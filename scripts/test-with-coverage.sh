#!/usr/bin/env bash
# Runs every test, merges coverage from all test projects, and fails when line coverage
# of Pfm.Domain and Pfm.Application together is below the floor (spec section 13).
# Usage: scripts/test-with-coverage.sh [extra dotnet test arguments, e.g. -c Release --no-build]
set -euo pipefail

FLOOR="${COVERAGE_FLOOR:-85}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RESULTS="$ROOT/TestResults"
REPORT="$RESULTS/coverage"

rm -rf "$RESULTS"
dotnet test "$ROOT/Pfm.slnx" --settings "$ROOT/coverage.runsettings" --collect "XPlat Code Coverage" \
  --results-directory "$RESULTS" "$@"

dotnet tool restore >/dev/null
dotnet reportgenerator -reports:"$RESULTS/**/coverage.cobertura.xml" -targetdir:"$REPORT" \
  -reporttypes:"JsonSummary;TextSummary;MarkdownSummaryGithub" -verbosity:Warning

cat "$REPORT/Summary.txt"

python3 - "$REPORT/Summary.json" "$FLOOR" <<'PY'
import json, sys
summary = json.load(open(sys.argv[1]))["summary"]
floor = float(sys.argv[2])
coverable = summary.get("coverablelines") or 0
if coverable == 0:
    print("No coverable lines in Domain or Application yet; coverage floor not applied.")
    sys.exit(0)
coverage = float(summary["linecoverage"])
print(f"Line coverage of Domain and Application: {coverage:.1f}% (floor {floor:g}%)")
sys.exit(0 if coverage >= floor else 1)
PY
