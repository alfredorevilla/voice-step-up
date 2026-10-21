# ADR-0004: Accept fresh Entra MFA as the fallback

Status: Proposed (pending the live spike, M5 T5.4)
Date: 2026-10-20
Decisions: D6

## Context
When voice fails or is unavailable the user needs a non-biometric option. The fallback is Entra MFA re-authentication, behind `IFallbackFactor`.

## Decision
Accept the fallback when the Entra access token shows fresh MFA: `amr` contains `mfa` or `ngcmfa`, and `auth_time` is within N minutes (default 10, configurable). `iat` is not a proxy: a silent refresh gives a new `iat` while `amr` keeps the old MFA. If `auth_time` is missing the rule fails closed. The rule lives behind `IFallbackFactor` and configuration so the spike can change it without touching policy code. The same rule gates enrollment and replacement of a voiceprint.

The API app registration declares `optionalClaims.accessToken` = `amr`, `auth_time` and `requestedAccessTokenVersion: 2` (M5 Bicep scope). The SPA asks for fresh MFA with the claims request `{"access_token":{"amr":{"essential":true,"values":["ngcmfa"]}}}` through `msal-browser` (`acquireTokenRedirect` or Popup, which skip the token cache).

## Alternatives considered
- Conditional Access authentication context (`acrs`), the documented step-up mechanism: rejected for now because it moves policy into Entra, against the settled decision to keep policy in the C# service. Revisit if the spike fails.
- `prompt=login` plus `auth_time`: the other fallback if the claims request is not honored.
- Separate TOTP built into the service: duplicates what Entra provides.
- No fallback: violates the NIST alternative-method requirement.

## Consequences
- UNVERIFIED until T5.4, which tests on the custom API audience: (a) the claims request forces a new MFA prompt during a live SSO session, (b) `ngcmfa` comes back for a TOTP-only or FIDO2-only user (without the request, `ngcmfa` normally appears only for Authenticator push and Windows Hello for Business), (c) `auth_time` moves forward after the re-prompt.
- Learn documents the `ngcmfa` claims request for Microsoft Graph authentication-methods calls; its use for a custom API is not documented.
- Tokens from the fake authority carry the same claims, and its authorize endpoint honors the claims request, so CI and the offline demo can test the rule.
