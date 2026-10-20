#!/usr/bin/env bash
# Print the current 6-digit TOTP code for the Entra test user, and nothing else.
# The secret file stays out of Claude's command text so a deny rule can block direct reads.
set -euo pipefail

secret_file="${VOICE_STEPUP_TOTP_FILE:-$HOME/.config/voice-step-up/totp-secret}"

[ -r "$secret_file" ] || { echo "voice-totp: secret file not readable" >&2; exit 1; }
command -v oathtool >/dev/null || { echo "voice-totp: oathtool not installed" >&2; exit 1; }

# A code from the last 3 seconds of a 30 s window can expire before the form submits.
s=$(( $(date +%s) % 30 ))
[ "$s" -ge 27 ] && sleep $(( 30 - s + 1 ))

oathtool --totp -b "$(tr -d ' \n' < "$secret_file")"
