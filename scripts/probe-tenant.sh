#!/usr/bin/env bash
# Read-only probe of the Entra tenant and tooling. Makes no changes.
# Needs VOICE_STEPUP_TENANT (tenant id) and VOICE_STEPUP_SUBSCRIPTION (subscription id),
# from the environment or from .env.local at the repo root (gitignored).
# Report: artifacts/probe-tenant.report.txt (gitignored: it contains tenant details).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[ -f "$root/.env.local" ] && . "$root/.env.local"

: "${VOICE_STEPUP_TENANT:?set VOICE_STEPUP_TENANT (tenant id)}"
: "${VOICE_STEPUP_SUBSCRIPTION:?set VOICE_STEPUP_SUBSCRIPTION (subscription id)}"

# Published SHA-256 of Bicep v0.48.1 (GitHub release API, asset digests).
BICEP_VERSION=0.48.1
BICEP_SHA256_LINUX=b09ec25a9d376c1f8e33ede6ed22b587f915ad68488d5db77a6f9541748c7f6e
BICEP_SHA256_WIN=398af294cf16ac4becdbd008a77d148d0e8e91492309a6b60ec911b7e1cab082

az_bin="${AZ:-az}"
if ! command -v "$az_bin" >/dev/null 2>&1; then
  az_bin="/mnt/c/Program Files/Microsoft SDKs/Azure/CLI2/wbin/az"
fi

azq() { "$az_bin" "$@" 2>&1 | tr -d '\r'; }

mkdir -p "$root/artifacts"
report="$root/artifacts/probe-tenant.report.txt"
fail=0
unmet=()

say() { printf '%s\n' "$*" | tee -a "$report"; }
: > "$report"
say "probe-tenant $(date -u +%Y-%m-%dT%H:%M:%SZ)"

# 1. Tenant assertion.
tenant="$(azq account show --subscription "$VOICE_STEPUP_SUBSCRIPTION" --query tenantId -o tsv)"
if [ "$tenant" = "$VOICE_STEPUP_TENANT" ]; then
  say "PASS tenant matches VOICE_STEPUP_TENANT"
else
  say "FAIL tenant mismatch (got: ${tenant:-none})"
  say "report: $report"
  exit 1
fi

# 2. Subscriptions visible in this tenant.
say "subscriptions in tenant:"
azq account list --query "[?tenantId=='$VOICE_STEPUP_TENANT'].{name:name,state:state}" -o tsv | sed 's/^/  /' | tee -a "$report"

# 3. Can the signed-in user register apps? (needs Policy.Read.All; may be denied)
allowed="$(azq rest --method GET --url 'https://graph.microsoft.com/v1.0/policies/authorizationPolicy' \
  --query 'defaultUserRolePermissions.allowedToCreateApps' -o tsv)"
case "$allowed" in
  true)  say "PASS allowedToCreateApps = true" ;;
  false) say "WARN allowedToCreateApps = false (needs the Application Developer role)"; unmet+=("P4: user cannot create apps by default") ;;
  *)     say "UNKNOWN allowedToCreateApps (read refused or unavailable)" ;;
esac

# 4. Directory roles of the signed-in user.
roles="$(azq rest --method GET --url 'https://graph.microsoft.com/v1.0/me/transitiveMemberOf/microsoft.graph.directoryRole' \
  --query 'value[].displayName' -o tsv)"
say "directory roles of signed-in user: ${roles:-none or unreadable}"
case "$roles" in
  *"Application Developer"*|*"Application Administrator"*|*"Cloud Application Administrator"*|*"Global Administrator"*)
    say "PASS holds an app-registration role" ;;
  *)
    if [ "$allowed" != "true" ]; then unmet+=("P4: no app-registration role and allowedToCreateApps is not true"); fi ;;
esac

# 5. Installed Bicep binary against the published digest.
bicep_bin="${BICEP_BIN:-}"
if [ -z "$bicep_bin" ]; then
  if win_home="$(cmd.exe /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r')" && [ -n "$win_home" ]; then
    bicep_bin="$(wslpath "$win_home" 2>/dev/null)/.azure/bin/bicep.exe"
  fi
  [ -f "${bicep_bin:-}" ] || bicep_bin="$HOME/.azure/bin/bicep"
fi
if [ -f "$bicep_bin" ]; then
  case "$bicep_bin" in *.exe) want="$BICEP_SHA256_WIN" ;; *) want="$BICEP_SHA256_LINUX" ;; esac
  got="$(sha256sum "$bicep_bin" | cut -d' ' -f1)"
  if [ "$got" = "$want" ]; then
    say "PASS bicep $BICEP_VERSION sha256 matches the published digest"
  else
    say "FAIL bicep sha256 differs from the published v$BICEP_VERSION digest"
    fail=1
  fi
else
  say "FAIL bicep binary not found (set BICEP_BIN)"
  fail=1
fi

# Summary.
if [ "${#unmet[@]}" -gt 0 ]; then
  say "unmet prerequisites:"
  printf '  - %s\n' "${unmet[@]}" | tee -a "$report"
else
  say "unmet prerequisites: none"
fi
say "report: $report"
exit "$fail"
