#!/usr/bin/env bash
# Delete the two project app registrations by exact uniqueName, then purge them from
# directory/deletedItems (a soft-deleted app keeps its uniqueName and blocks a redeploy).
# Usage: scripts/entra-teardown.sh [--dry-run] [--yes]
#   --dry-run  assert the tenant and list what would be deleted; change nothing.
#   --yes      skip the confirmation prompt.
# Each service principal is deleted and purged explicitly before its application.
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

# Graph GET returning one value (--query), empty only on a Graph "not found". Any other failure
# (expired login, no permission) stops the script, so it cannot report a false "nothing to delete"
# or a false "teardown verified".
graph_value() {
  local url="$1" query="$2" out
  if out="$(azq rest --method GET --url "$url" --query "$query" -o tsv 2>&1)"; then
    printf '%s' "$out"
  elif printf '%s' "$out" | /usr/bin/grep -qiE 'Request_ResourceNotFound|NotFound|404'; then
    printf ''
  else
    echo "Graph lookup failed for $url: $(printf '%s' "$out" | head -c 300)" >&2
    exit 1
  fi
}

# Live application object id by its exact uniqueName (alternate key).
live_object_id() { graph_value "$graph/applications(uniqueName='$1')?\$select=id" id; }
# appId of an application object id.
app_id_of() { graph_value "$graph/applications/$1?\$select=appId" appId; }
# Live service principal object id by appId (alternate key).
sp_object_id() { graph_value "$graph/servicePrincipals(appId='$1')?\$select=id" id; }
# Id of a soft-deleted directory object, empty if it is not in deletedItems.
deleted_item_id() { graph_value "$graph/directory/deletedItems/$1?\$select=id" id; }

# Delete a live object, then purge it from deletedItems. Matched by id captured before deletion.
delete_and_purge() {
  azq rest --method DELETE --url "$graph/$1/$2" > /dev/null
  azq rest --method DELETE --url "$graph/directory/deletedItems/$2" > /dev/null
}

targets=(); app_ids=(); sp_ids=()
for name in "$api_unique_name" "$spa_unique_name"; do
  id="$(live_object_id "$name")"
  if [ -n "$id" ]; then
    app_id="$(app_id_of "$id")"
    sp_id="$(sp_object_id "$app_id")"
    echo "will delete application uniqueName=$name objectId=$id servicePrincipal=${sp_id:-none}"
    targets+=("$id"); app_ids+=("$app_id"); sp_ids+=("$sp_id")
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

for i in "${!targets[@]}"; do
  if [ -n "${sp_ids[$i]}" ]; then
    delete_and_purge servicePrincipals "${sp_ids[$i]}"
    echo "deleted and purged service principal ${sp_ids[$i]}"
  fi
  delete_and_purge applications "${targets[$i]}"
  echo "deleted and purged application ${targets[$i]}"
done

# Verify by exact key and id: nothing live, nothing left in deletedItems.
for name in "$api_unique_name" "$spa_unique_name"; do
  [ -z "$(live_object_id "$name")" ] || { echo "still present: uniqueName=$name" >&2; exit 1; }
done
for i in "${!targets[@]}"; do
  [ -z "$(sp_object_id "${app_ids[$i]}")" ] || { echo "service principal still present: appId=${app_ids[$i]}" >&2; exit 1; }
  [ -z "$(deleted_item_id "${targets[$i]}")" ] || { echo "application still in deletedItems: ${targets[$i]}" >&2; exit 1; }
  if [ -n "${sp_ids[$i]}" ]; then
    [ -z "$(deleted_item_id "${sp_ids[$i]}")" ] || { echo "service principal still in deletedItems: ${sp_ids[$i]}" >&2; exit 1; }
  fi
done
echo "teardown verified"
