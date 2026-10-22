#!/usr/bin/env bash
# Fetch the models listed in models/models.lock and check every SHA-256. Models are never committed.
# Usage: scripts/fetch-models.sh [--test]    (--test also fetches the models only the Category=Model tests use)
# Exit codes: 0 all present and verified; 1 download or checksum failure.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
models="${MODELS_DIR:-$root/models}"
lock="$root/models/models.lock"
with_test=0
[ "${1:-}" = "--test" ] && with_test=1

mkdir -p "$models"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

sha_of() { sha256sum "$1" | cut -d' ' -f1; }

# True when every "verify" line under directory $1 (relative to models/) is present with the right hash.
dir_verified() {
  local ok=1 scope kind sha path _
  while read -r scope kind sha path _; do
    [ "$kind" = "verify" ] || continue
    [[ "$path" == "$1/"* ]] || continue
    if [ ! -f "$models/$path" ] || [ "$(sha_of "$models/$path")" != "$sha" ]; then ok=0; fi
  done < "$lock"
  [ "$ok" -eq 1 ]
}

while read -r scope kind sha path url; do
  case "${scope:-}" in ''|'#'*) continue ;; esac
  if [ "$scope" = "test" ] && [ "$with_test" -eq 0 ]; then continue; fi

  case "$kind" in
    download)
      dest="$models/$path"
      if [[ "$path" == *.tar.bz2 ]]; then
        dir="$models/${path%.tar.bz2}"
        marker="$dir/.fetched-$sha"
        if [ -f "$marker" ] && dir_verified "${path%.tar.bz2}"; then echo "ok (present) $path"; continue; fi
        archive="$tmp/$path"
        curl -fL --retry 3 -sS -o "$archive" "$url"
        got="$(sha_of "$archive")"
        if [ "$got" != "$sha" ]; then echo "CHECKSUM MISMATCH $path: expected $sha got $got" >&2; exit 1; fi
        rm -rf "$dir"; mkdir -p "$dir"
        tar -xjf "$archive" -C "$dir" --strip-components=1
        : > "$marker"
        echo "ok (fetched) $path"
      else
        if [ -f "$dest" ] && [ "$(sha_of "$dest")" = "$sha" ]; then echo "ok (present) $path"; continue; fi
        curl -fL --retry 3 -sS -o "$tmp/file" "$url"
        got="$(sha_of "$tmp/file")"
        if [ "$got" != "$sha" ]; then echo "CHECKSUM MISMATCH $path: expected $sha got $got" >&2; exit 1; fi
        mkdir -p "$(dirname "$dest")"
        mv "$tmp/file" "$dest"
        echo "ok (fetched) $path"
      fi
      ;;
    verify)
      if [ ! -f "$models/$path" ]; then echo "MISSING $path" >&2; exit 1; fi
      got="$(sha_of "$models/$path")"
      if [ "$got" != "$sha" ]; then echo "CHECKSUM MISMATCH $path: expected $sha got $got" >&2; exit 1; fi
      ;;
    *)
      echo "unknown kind '$kind' in models.lock" >&2; exit 1 ;;
  esac
done < "$lock"

echo "models verified"
