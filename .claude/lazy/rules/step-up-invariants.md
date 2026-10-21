---
on:
  tools: [Edit, Write]
  paths: ["src/**/*", "tests/**/*", "web/src/**/*"]
---
# Step-Up Invariants

These are the design invariants of the service. A change that breaks one needs an ADR first. Threat ids refer to `docs/threat-model.md`.

## Custom rules

### Required

Voice MUST NOT be the only factor. No endpoint may accept a step-up assertion unless the request also carries a valid bearer token for the same user (same `oid` and `tid`).

Any verifier failure, exception, timeout or cancellation MUST produce the fallback instruction. It MUST NOT produce a pass.

The `amr: ["vbm"]` assertion MUST be issued only after a verifier pass whose score is at or above the stored threshold and a passing phrase check.

A challenge MUST be single use, bound to the user and action, and expire after its TTL.

Enrolling or replacing a voiceprint MUST require recorded consent and fresh MFA (TM-003).

The assertion MUST be accepted once: its `jti` is consumed when the protected action runs. The validator MUST check `iss`, `aud`, `oid` and `tid`, and pin the algorithm to ES256 (TM-006).

The assertion MUST carry its own `aud` and `typ` (`stepup+jwt`), distinct from Entra tokens. The assertion check MUST reject Entra tokens, and the Entra bearer handler MUST reject the assertion (TM-007).

Thresholds MUST come from the threshold store. No threshold and no per-user override may be hardcoded. Tests and seed data MAY set thresholds through the store. A threshold MUST be within 0.0 to 1.0, and every change MUST record `UpdatedBy` (TM-004).

Every attempt, pass or fail, MUST be recorded in the Attempts table with its result, and with a reason code on failure.

"Log" means two things. The Attempts table MAY hold the score. Application logs and telemetry MUST NOT hold audio, transcript, embeddings or scores. The Attempts table MUST NOT hold audio or embeddings.

The SPA MUST use the authorization code flow with PKCE. It MUST NOT use a client secret or the implicit grant.

The fresh-MFA fallback rule is UNVERIFIED until the M5 T5.4 spike (ADR-0004). It requires `auth_time` within the window and does not accept `iat`. Do not hardcode `ngcmfa` handling before the spike.

### Recommended

The SPA SHOULD NOT keep tokens in `localStorage`.
