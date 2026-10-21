#!/usr/bin/env bash
# Deploy the Entra app registrations (infra/entra.bicep) and write the client ids to .env.local.
# Usage: scripts/entra-deploy.sh [--dry-run] [--yes]
#   --dry-run  assert the tenant (read-only), build the template, print the command; change nothing.
#   --yes      skip the confirmation prompt.
# Graph Bicep has no what-if, so --dry-run cannot preview the tenant change.
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

template="$root/infra/entra.bicep"
params="$root/infra/main.bicepparam"

assert_tenant

azq bicep build --file "$(winpath "$template")" --stdout > /dev/null
echo "template builds"

echo "will deploy to resource group $resource_group:"
echo "  apps: $api_unique_name, $spa_unique_name (names from the parameter file)"
if [ "$dry_run" = 1 ]; then
  echo "dry run: nothing deployed"
  exit 0
fi

if [ "$assume_yes" != 1 ]; then
  read -r -p "Deploy to the tenant? [y/N] " answer
  [ "$answer" = "y" ] || { echo "aborted"; exit 1; }
fi

outputs="$(azq deployment group create \
  --subscription "$VOICE_STEPUP_SUBSCRIPTION" \
  --resource-group "$resource_group" \
  --name voice-step-up-entra \
  --template-file "$(winpath "$template")" \
  --parameters "$(winpath "$params")" \
  --query properties.outputs -o json)"

api_client_id="$(printf '%s' "$outputs" | python3 -c 'import json,sys; print(json.load(sys.stdin)["apiClientId"]["value"])')"
spa_client_id="$(printf '%s' "$outputs" | python3 -c 'import json,sys; print(json.load(sys.stdin)["spaClientId"]["value"])')"
api_scope="$(printf '%s' "$outputs" | python3 -c 'import json,sys; print(json.load(sys.stdin)["apiScope"]["value"])')"

set_env_local VOICE_STEPUP_API_CLIENT_ID "$api_client_id"
set_env_local VOICE_STEPUP_SPA_CLIENT_ID "$spa_client_id"
set_env_local VOICE_STEPUP_API_SCOPE "$api_scope"
echo "deployed; client ids and scope written to .env.local (not printed)"
