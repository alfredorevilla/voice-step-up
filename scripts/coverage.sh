#!/usr/bin/env bash
# Line coverage of StepUp.Core and StepUp.Api (M7-A3), merged across all test projects: a line counts as covered
# when any test project executes it. Model-trait tests are excluded, as in CI.
# Usage: scripts/coverage.sh [output-dir]      (default: artifacts/coverage, gitignored)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${1:-$root/artifacts/coverage}"
rm -rf "$out"
mkdir -p "$out"

cd "$root"
dotnet test -- --filter-not-trait "Category=Model" \
  --coverage --coverage-output-format cobertura --coverage-output "$out/{assembly}.cobertura.xml" > "$out/test.log" 2>&1 \
  || { tail -30 "$out/test.log"; echo "tests failed; no coverage reported" >&2; exit 1; }

python3 -I - "$out" <<'PY'
import glob, sys
import xml.etree.ElementTree as ET

out = sys.argv[1]
wanted = ("StepUp.Core", "StepUp.Api")
hit: dict[tuple[str, str, int], bool] = {}
for path in glob.glob(f"{out}/*.cobertura.xml"):
    for package in ET.parse(path).getroot().iter("package"):
        name = package.get("name", "")
        if name not in wanted:
            continue
        for cls in package.iter("class"):
            file = cls.get("filename", "")
            for line in cls.iter("line"):
                key = (name, file, int(line.get("number", "0")))
                hit[key] = hit.get(key, False) or int(line.get("hits", "0")) > 0

for name in wanted:
    lines = [covered for (pkg, _, _), covered in hit.items() if pkg == name]
    total, covered = len(lines), sum(lines)
    pct = 100 * covered / total if total else 0
    print(f"{name}: {covered}/{total} lines = {pct:.1f}%")
PY
