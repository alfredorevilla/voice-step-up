# ADR-0005: Signed step-up assertion with amr vbm

Status: Accepted
Date: 2026-10-20
Decisions: D5

## Context
On a voice pass the service must issue a verifiable "step-up done" result. Voice must never be the only factor. `vbm` ("Biometric with voiceprint") is the identifier Entra's external-MFA documentation lists, so the spec's "voice" maps to it (decision of 2026-10-20).

## Decision
Issue a JWT: header `typ` = `stepup+jwt`, `alg` = ES256, `kid`; claims `iss` = this service, `aud` = the service's step-up audience (never the Entra App ID URI), `sub` = `oid`, `tid`, `amr:["vbm"]`, `act` = the action, optional `ph` = hash of the request's key fields (`amount`, `to`), `jti`, `iat`, `exp` (5 minutes).
- Single use: the `jti` is recorded in `UsedAssertions` when the step-up filter validates the assertion, just before the protected action runs.
- Valid only together with a valid bearer token for the same `oid` and `tid`, and only for its `act`.
- Own handler: only the service's key set (never the Entra JWKS), `ValidAlgorithms` = ES256, a required `typ`, rejects `none`, HS256 and an unknown `kid`. The Entra bearer handler rejects the assertion, and this handler rejects Entra tokens. A `vbm` value inside an Entra token is never treated as the voice step-up.
- Key: generated at first start, protected with Data Protection in the key-ring volume, never in the repo or image. Rotation is manual (delete and restart); stated as a demo limit.
- The issuer keeps claim and signing logic out of the HTTP layer.

## Alternatives considered
- Server-side session flag: not portable, not verifiable by another service.
- Reuse the Entra token and add a claim: Entra tokens cannot be edited.
- Opaque random token in a table: needs a lookup on each use and cannot be verified offline.
- HS256 with a shared secret: pushes key sharing onto every verifier.

## Consequences
- The signing key must be protected; key handling is in the threat model (TM-014).
- A stolen bearer plus assertion in the same session still works inside 5 minutes for the same action (TM-016); the binding stops cross-user and cross-action use.
- An assertion issued before voiceprint deletion stays valid up to 5 minutes.
