#!/usr/bin/env bash
# Run the demo against the real Entra tenant: the API in Entra mode with the real speaker verifier, and the built SPA
# signing in through Entra. Needs the app registrations (scripts/entra-deploy.sh) and the models (scripts/fetch-models.sh).
# Tenant and client ids come from the untracked .env.local and are never printed. Stop with Ctrl+C.
set -euo pipefail

# shellcheck source=lib-entra.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib-entra.sh"

: "${VOICE_STEPUP_API_CLIENT_ID:?run scripts/entra-deploy.sh first (VOICE_STEPUP_API_CLIENT_ID is missing from .env.local)}"
: "${VOICE_STEPUP_SPA_CLIENT_ID:?run scripts/entra-deploy.sh first (VOICE_STEPUP_SPA_CLIENT_ID is missing from .env.local)}"
: "${VOICE_STEPUP_API_SCOPE:?run scripts/entra-deploy.sh first (VOICE_STEPUP_API_SCOPE is missing from .env.local)}"

if [ ! -f "$root/models/wespeaker_en_voxceleb_resnet34_LM.onnx" ] || [ ! -d "$root/models/whisper-base.en" ]; then
  echo "the speaker and speech models are missing: run scripts/fetch-models.sh first" >&2
  exit 1
fi

api_port=5000
web_port=5173
data="$(mktemp -d)"
pids=()
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  rm -rf "$data"
}
trap cleanup EXIT INT TERM

echo "building the SPA for Entra sign-in"
(
  cd "$root/web"
  [ -d node_modules ] || npm ci
  VITE_AUTH_MODE=entra \
  VITE_API_BASE="http://localhost:$api_port" \
  VITE_AUTHORITY="https://login.microsoftonline.com/$VOICE_STEPUP_TENANT" \
  VITE_CLIENT_ID="$VOICE_STEPUP_SPA_CLIENT_ID" \
  VITE_API_SCOPE="$VOICE_STEPUP_API_SCOPE" \
    npm run build
)

echo "starting the API on http://localhost:$api_port (Entra mode, real verifier)"
(
  cd "$root"
  # The audience is the App ID URI: the scope without its last segment.
  Auth__Mode=Entra \
  AzureAd__TenantId="$VOICE_STEPUP_TENANT" \
  AzureAd__ClientId="$VOICE_STEPUP_API_CLIENT_ID" \
  AzureAd__Audience="${VOICE_STEPUP_API_SCOPE%/*}" \
  Verifier__Mode=Sherpa \
  Verifier__Sherpa__ModelsDir="$root/models" \
  Storage__DatabasePath="$data/stepup.db" \
  Storage__KeyDirectory="$data/keys" \
  ASPNETCORE_URLS="http://localhost:$api_port" \
    exec dotnet run --project src/StepUp.Api -c Release
) &
pids+=("$!")

echo "serving the SPA on http://localhost:$web_port"
(
  cd "$root/web"
  exec npx vite preview --port "$web_port" --strictPort
) &
pids+=("$!")

echo "open http://localhost:$web_port and sign in with the test user (Ctrl+C stops both)"
wait
