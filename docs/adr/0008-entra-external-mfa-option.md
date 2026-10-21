# ADR-0008: Entra external MFA is a documented option, not built

Status: Proposed
Date: 2026-10-20
Decisions: D11

## Context
Entra can call an external provider as an MFA method. Microsoft Learn describes a Conditional Access "Require multifactor authentication" policy, an OIDC discovery document, an authorization endpoint taking `id_token_hint`, a `jwks_uri` with `x5c`, RS256, one `acr` and one `amr` (`vbm`) in the returned `id_token`, public HTTPS, admin consent, and a tenant-wide authentication-methods change (scope it to a test group).

## Decision
Do not build it. Document it as an option and keep the assertion issuer (ADR-0005) separable so it could back those endpoints later.

## Alternatives considered
- Build it now: needs public HTTPS and tenant-wide changes, and conflicts with the offline default.
- Ignore it: loses the honest statement of where this pattern would go in production.

## Consequences
- Licensing and availability in the user's tenant are UNVERIFIED.
- If pursued, it is a separate plan.
