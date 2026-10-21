# ADR-0005: Signed step-up assertion with amr vbm

Status: Proposed
Date: 2026-10-20
Decisions: D5

## Context
On a voice pass the service must issue a verifiable "step-up done" result. Voice must never be the only factor. `vbm` ("Biometric with voiceprint") is the identifier Entra's external-MFA documentation lists, so the spec's "voice" maps to it (decision of 2026-10-20).

## Decision
Issue a JWT signed with ES256: `amr:["vbm"]`, bound to the Entra `oid` and `tid`, with `jti` and a 5-minute lifetime. It counts only together with a valid bearer token for the same user. The issuer keeps claim and signing logic out of the HTTP layer so it can later back OIDC endpoints (ADR-0008).

## Alternatives considered
- Server-side session flag: not portable, not verifiable by another service.
- Reuse the Entra token and add a claim: Entra tokens cannot be edited.
- Opaque random token in a table: needs a lookup on each use and cannot be verified offline.

## Consequences
- The signing key must be protected and rotated; key handling is in the threat model.
- Replay inside 5 minutes is limited by `jti` tracking and the bearer binding.
