#!/usr/bin/env bash
# Reproduce the M6 evaluation (docs/design/evaluation-protocol.md).
# Usage: scripts/eval.sh [fetch|run|all]   (default: all)
#   fetch  download LibriSpeech dev-clean and test-clean, check the MD5, extract (git-ignored folder)
#   run    fetch models, build the tool, run the protocol, write docs/eval-report.md and docs/eval/*.csv
# Needs: dotnet, ffmpeg, curl, md5sum, tar. Audio is never committed.
# Environment: EVAL_DATA (default <repo>/eval-data), EVAL_SET to limit to one set (dev-clean or test-clean).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
data="${EVAL_DATA:-$root/eval-data}"
sets=(dev-clean test-clean)
[ -n "${EVAL_SET:-}" ] && sets=("$EVAL_SET")

fetch_set() {
  local name="$1" archive expected got
  archive="$data/dl/$name.tar.gz"
  expected="$(awk -v n="$name.tar.gz" '$2 == n { print $1 }' "$root/eval/librispeech.md5")"
  [ -n "$expected" ] || { echo "no pinned md5 for $name" >&2; return 1; }

  if [ -d "$data/LibriSpeech/$name" ] && [ -f "$data/LibriSpeech/$name/.verified" ]; then
    echo "ok (present) $name"
    return 0
  fi

  mkdir -p "$data/dl"
  if [ ! -f "$archive" ] || [ "$(md5sum "$archive" | cut -d' ' -f1)" != "$expected" ]; then
    curl -4 -fsSL -C - --retry 5 --retry-delay 5 -o "$archive" "https://www.openslr.org/resources/12/$name.tar.gz"
  fi
  got="$(md5sum "$archive" | cut -d' ' -f1)"
  if [ "$got" != "$expected" ]; then
    echo "md5 mismatch for $name (expected $expected, got $got)" >&2
    return 1
  fi
  tar -xzf "$archive" -C "$data"
  touch "$data/LibriSpeech/$name/.verified"
  echo "ok (fetched, md5 verified) $name"
}

fetch() {
  command -v ffmpeg >/dev/null || { echo "ffmpeg is required to decode FLAC" >&2; return 1; }
  local s
  for s in "${sets[@]}"; do fetch_set "$s"; done
}

run() {
  "$root/scripts/fetch-models.sh"
  local protocol
  local first latest
  first="$(git -C "$root" log --diff-filter=A --format=%h -- docs/design/evaluation-protocol.md 2>/dev/null | tail -1)"
  latest="$(git -C "$root" log -1 --format=%h -- docs/design/evaluation-protocol.md 2>/dev/null)"
  if [ -z "$first" ]; then protocol="unknown"
  elif [ "$first" = "$latest" ]; then protocol="\`$first\`"
  else protocol="\`$first\` (amended in \`$latest\`)"; fi
  dotnet run -c Release --project "$root/eval/StepUp.Eval" -- \
    --data "$data" --models "$root/models" --out "$root/docs" --protocol-commit "$protocol" "${sets[@]/#/--set=}"
}

case "${1:-all}" in
  fetch) fetch ;;
  run) run ;;
  all) fetch; run ;;
  *) echo "usage: $0 [fetch|run|all]" >&2; exit 2 ;;
esac
