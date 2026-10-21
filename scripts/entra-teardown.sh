#!/usr/bin/env bash
# Delete the two project app registrations by exact uniqueName, then purge them from
# directory/deletedItems (a soft-deleted app keeps its uniqueName and blocks a redeploy).
# Usage: scripts/entra-teardown.sh [--dry-run] [--yes]
#   --dry-run  assert the tenant and list what would be deleted; change nothing.
#   --yes      skip the confirmation prompt.
# Service principals go with their application.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$here/lib-entra.sh"

dry_run=0; assume_yes=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) dry_run=1 ;;
    --yes) assume_yes=1 ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

graph="https://graph.microsoft.com/v1.0"

assert_tenant

# Object id of a live application by its exact uniqueName (alternate key), empty if none.
live_object_id() {
  azq rest --method GET --url "$graph/applications(uniqueName='$1')?\$select=id" --query id -o tsv 2>/dev/null || true
}

# Object ids of soft-deleted applications. Matched by id captured before deletion, never by name.
purge() {
  azq rest --method DELETE --url "$graph/directory/deletedItems/$1" > /dev/null
}

targets=()
for name in "$api_unique_name" "$spa_unique_name"; do
  id="$(live_object_id "$name")"
  if [ -n "$id" ]; then
    echo "will delete application uniqueName=$name objectId=$id"
    targets+=("$id")
  else
    echo "not found (already gone): uniqueName=$name"
  fi
done

if [ "${#targets[@]}" -eq 0 ]; then
  echo "nothing to delete"
  exit 0
fi

if [ "$dry_run" = 1 ]; then
  echo "dry run: nothing deleted"
  exit 0
fi

if [ "$assume_yes" != 1 ]; then
  read -r -p "Delete these applications and purge them? [y/N] " answer
  [ "$answer" = "y" ] || { echo "aborted"; exit 1; }
fi

for id in "${targets[@]}"; do
  azq rest --method DELETE --url "$graph/applications/$id" > /dev/null
  purge "$id"
  echo "deleted and purged $id"
done

# Verify by the exact key: both lookups must now find nothing.
for name in "$api_unique_name" "$spa_unique_name"; do
  [ -z "$(live_object_id "$name")" ] || { echo "still present: uniqueName=$name" >&2; exit 1; }
done
echo "teardown verified"
