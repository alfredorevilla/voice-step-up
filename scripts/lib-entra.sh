#!/usr/bin/env bash
# Shared helpers for entra-deploy.sh and entra-teardown.sh. Source it; do not run it.
# Reads VOICE_STEPUP_TENANT and VOICE_STEPUP_SUBSCRIPTION from the environment or the
# gitignored .env.local at the repo root.

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Variables already set in the environment win over .env.local.
if [ -f "$root/.env.local" ]; then
  while IFS='=' read -r key value; do
    case "$key" in ''|\#*) continue ;; esac
    if [ -z "${!key+x}" ]; then export "$key=$value"; fi
  done < "$root/.env.local"
fi

: "${VOICE_STEPUP_TENANT:?set VOICE_STEPUP_TENANT (tenant id)}"
: "${VOICE_STEPUP_SUBSCRIPTION:?set VOICE_STEPUP_SUBSCRIPTION (subscription id)}"

resource_group="${VOICE_STEPUP_RG:-rg-voice-step-up}"
api_unique_name="${VOICE_STEPUP_API_UNIQUE_NAME:-voice-step-up-api}"
spa_unique_name="${VOICE_STEPUP_SPA_UNIQUE_NAME:-voice-step-up-spa}"

az_bin="${AZ:-az}"
if ! command -v "$az_bin" >/dev/null 2>&1; then
  az_bin="/mnt/c/Program Files/Microsoft SDKs/Azure/CLI2/wbin/az"
fi

# The wrapper must not be named az: it would call itself through "$az_bin" when az is on PATH.
azq() { "$az_bin" "$@" 2> >(tr -d '\r' >&2) | tr -d '\r'; }

# Template paths for the Windows az.
winpath() { if command -v wslpath >/dev/null 2>&1; then wslpath -w "$1"; else printf '%s' "$1"; fi; }

# Refuse to continue unless the signed-in tenant is the configured one.
assert_tenant() {
  local tenant
  tenant="$(azq account show --subscription "$VOICE_STEPUP_SUBSCRIPTION" --query tenantId -o tsv)"
  if [ "$tenant" != "$VOICE_STEPUP_TENANT" ]; then
    echo "refusing: signed-in tenant does not match VOICE_STEPUP_TENANT (got: ${tenant:-none})" >&2
    exit 1
  fi
  echo "tenant assertion passed"
}

# Replace or add KEY=value in .env.local without echoing other lines.
set_env_local() {
  local key="$1" value="$2" file="$root/.env.local" tmp
  tmp="$(mktemp)"
  { [ -f "$file" ] && /usr/bin/grep -v "^${key}=" "$file"; printf '%s=%s\n' "$key" "$value"; } > "$tmp"
  mv "$tmp" "$file"
}
