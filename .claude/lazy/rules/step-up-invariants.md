---
on:
  tools: [Edit, Write]
  paths: ["src/**/*", "tests/**/*", "web/src/**/*"]
---
# Step-Up Invariants

These are the design invariants of the service. A change that breaks one needs an ADR first.

## Custom rules

### Required

Voice MUST NOT be the only factor. No endpoint may accept a step-up assertion unless the request also carries a valid bearer token for the same user (same `oid` and `tid`).

Any verifier failure, exception, timeout or cancellation MUST produce the fallback instruction. It MUST NOT produce a pass.

The `amr: ["vbm"]` assertion MUST be issued only after a verifier pass whose score is at or above the stored threshold and a passing phrase check.

A challenge MUST be single use, bound to the user and action, and expire after its TTL.

Thresholds MUST come from the threshold store. No threshold and no per-user override may be hardcoded.

Every attempt, pass or fail, MUST be logged with its reason code. The log MUST NOT hold audio or embeddings.

The SPA MUST use the authorization code flow with PKCE. It MUST NOT use a client secret or the implicit grant.

### Recommended

The SPA SHOULD NOT keep tokens in `localStorage`.

New `IVerifier` implementations SHOULD be added behind the shared contract test suite before any wiring.
