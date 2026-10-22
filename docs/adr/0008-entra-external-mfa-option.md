# ADR-0008: Entra external MFA is a documented option, not built

Status: Accepted
Date: 2026-10-20
Decisions: D11

## Context
Entra can call an external provider as an MFA method. Microsoft Learn describes a Conditional Access "Require multifactor authentication" policy, an OIDC discovery document, an authorization endpoint taking `id_token_hint`, a `jwks_uri` with `x5c`, RS256, one `acr` and one `amr` (`vbm`) in the returned `id_token`, public HTTPS, admin consent, and a tenant-wide authentication-methods change (scope it to a test group).

## Decision
Do not build it. Document it as an option. The external-MFA `id_token` needs a second token profile (RS256, `x5c` in the JWKS, `sub` taken from `id_token_hint`, Entra's `aud`, `nonce`, exactly one `acr` and one `amr`). It does not match the internal assertion (ES256, `sub` = `oid`, `act`), so only key handling and signing code would be reusable.

## Alternatives considered
- Build it now: needs public HTTPS and tenant-wide changes, and conflicts with the offline default.
- Ignore it: loses the honest statement of where this pattern would go in production.

## Consequences
- Licensing and availability in the user's tenant are UNVERIFIED.
- If pursued, it is a separate plan. Entra forwards external-provider `amr` values into its own tokens, so the payout check keeps ignoring `vbm` inside Entra tokens (ADR-0005).
