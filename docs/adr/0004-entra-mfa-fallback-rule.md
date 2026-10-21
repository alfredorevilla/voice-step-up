# ADR-0004: Accept fresh Entra MFA as the fallback

Status: Proposed (pending the live spike, M5 T5.4)
Date: 2026-10-20
Decisions: D6

## Context
When voice fails or is unavailable the user needs a non-biometric option. The fallback is Entra MFA re-authentication, behind `IFallbackFactor`.

## Decision
Accept the fallback when the Entra access token shows fresh MFA: `amr` includes `mfa` or `ngcmfa` and `auth_time` or `iat` is recent. The SPA asks for it with a claims request `{"access_token":{"amr":{"essential":true,"values":["ngcmfa"]}}}`.

## Alternatives considered
- Conditional Access authentication context: moves policy into Entra, against the settled decision to keep policy in the C# service.
- Separate TOTP built into the service: duplicates what Entra already provides.
- No fallback: violates the NIST alternative-method requirement.

## Consequences
- Whether Entra puts these claims in the access token for this API is UNVERIFIED. The live spike confirms or changes the rule.
- Tokens for the Fake authority carry the same claims so CI can test the rule.
