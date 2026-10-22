# ADR-0004: Accept fresh Entra MFA as the fallback

Status: Accepted (live spike M5 T5.4 run 2026-10-21; see Spike result for its limits)
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
- T5.4 tested three questions on the custom API audience; see Spike result. The claims request forces a fresh MFA prompt and `auth_time` moves forward. For a TOTP user the token carries `mfa` and `totp`, not `ngcmfa`, so the rule must keep accepting `mfa`.
- Learn documents the `ngcmfa` claims request for Microsoft Graph authentication-methods calls; its use for a custom API is not documented.
- Tokens from the fake authority carry the same claims, and its authorize endpoint honors the claims request, so CI and the offline demo can test the rule.

## Spike result (M5 T5.4, 2026-10-21)

Run by the user and the agent together: the user typed the test user's username and password in a Playwright browser, the agent entered the TOTP code. Authorization code with PKCE against the real tenant as the SPA app, scope of the API app, one non-admin test user (TOTP only). Tokens were decoded in memory; only claim names, `amr` values and ages were recorded.

| Request | `amr` | `auth_time` age at exchange | Access token lifetime |
|---|---|---|---|
| 1. Plain sign-in | `pwd` | 50 s | about 1 hour |
| 2. Same browser session, claims request for `ngcmfa` | `pwd`, `totp`, `mfa` | 33 s | 600 s |

Answers:
- (a) **Forces fresh MFA: yes.** The plain request completed with a password only (the tenant did not require MFA for this user). The claims request in the same SSO session raised an "Enter code" prompt, and the token then carried `mfa`.
- (b) **`ngcmfa` for a TOTP user: no.** The token had `totp` and `mfa`. The `essential` request for a value that was not issued did not produce an error. The rule must therefore keep accepting `mfa`; `ngcmfa` would only add Authenticator-push and Windows Hello users.
- (c) **`auth_time` moves forward: yes.** The second token's `auth_time` was 33 s old although about two minutes had passed since the first sign-in.
- **Optional claims work.** `amr` and `auth_time` are present in the v2.0 access token for the API audience, with `scp` = `access_as_user`. No `roles` (none assigned to the test user) and no `idtyp` for the delegated token.
- **`iat` is not a freshness signal.** In both tokens `iat` was 300 s old at exchange regardless of `auth_time`, because Entra backdates it. This supports the decision never to use `iat`.

Limits: one user, one run, one browser session, no negative control such as `prompt=login` alone. The tenant does not require MFA for this user, so a plain sign-in gives no MFA. The deployed rule (`amr` has `mfa` or `ngcmfa`, `auth_time` recent, `iat` never) matches this evidence and is unchanged. Re-run the spike if the tenant's Conditional Access policy changes.
