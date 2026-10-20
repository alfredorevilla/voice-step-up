# Plan: voice-step-up, M2 Core domain, policy and API (fake verifier)

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M2 Core domain, policy and API (fake verifier)
**Complexity**: Medium
**Depends on**: M0, M1
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
The service without real audio: domain interfaces, challenge and assertion logic, persistence, the API, a fake OIDC authority and a shared IVerifier contract test suite.

## Patterns to Mirror
No application code exists yet, so there is nothing to mirror. Conventions come from the vendored skills, read when a task uses them.

## Files to Change
| Path | Action | Why |
|---|---|---|
| src/StepUp.Core, src/StepUp.Infrastructure, src/StepUp.Api, src/StepUp.FakeAuthority | CREATE | Service code |
| tests/StepUp.Core.Tests, tests/StepUp.Api.Tests, tests/StepUp.Verifier.Tests (contract suite) | CREATE | xUnit v3 suites |

## Tasks
- **T2.1** Interfaces and types in `StepUp.Core`: `IVerifier`, `IChallengeService`, `IStepUpPolicy`, `IFallbackFactor`, `IVoiceprintStore`, `IAttemptLog`, `IConsentStore`, `IThresholdStore`; reason codes (Muted, TooShort, Noisy, PhraseMismatch, BelowThreshold, Timeout, VerifierError, NoVoiceprint, ConsentMissing, ChallengeExpired, ChallengeConsumed, RateLimited).
- **T2.2** Challenge service (4-word CSPRNG phrase, single use, 60 s TTL, bound to user and action). TDD via `tdd-workflow`.
- **T2.3** Step-up assertion issuer/validator (D5): tamper, expiry, wrong user, replay, missing bearer tests.
- **T2.4** EF Core SQLite persistence, Data Protection encryption of embeddings, deletion path.
- **T2.5** API (`dotnet-webapi`; `Microsoft.Identity.Web` usage checked against Microsoft Learn MCP): `GET /api/me`; enrollment (`consent`, `start`, `sample`, `complete`); `DELETE /api/voiceprint`; `POST /api/stepup/challenge`, `POST /api/stepup/verify`; analyst `GET /api/analyst/attempts`, `GET|PUT /api/analyst/thresholds`; demo high-risk `POST /api/payouts`; `Auth:Mode` = Entra (`Microsoft.Identity.Web`) or Fake; `[RequiresStepUp]` policy; attempt limiting (after 5 consecutive failed voice attempts a delay of at least 30 s; after 50 voice is disabled for that user and the fallback is offered; these are the NIST SP 800-63B-4 section 3.2.3.3 limits for a system without presentation attack detection, configurable) plus general rate limiting; fail-closed verifier timeout (default 3 s) returning the fallback instruction.
- **T2.6** `StepUp.FakeAuthority`: discovery, JWKS, authorize/token for the SPA, and a token-mint helper for tests (claims `oid`, `tid`, `amr`, `auth_time`, `roles`).
- **T2.7** Abstract `IVerifier` contract test suite, run against a `FakeVerifier`.

## Validation
```bash
dotnet build && dotnet test   # via run-tests
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| Assertion design allows replay or cross-user use | Medium | M2-A2 tests: tamper, expiry, replay, other user, missing bearer |
| Rate and attempt limiting is wrong | Medium | M2-A7 tests with configurable limits |

## Acceptance
- [ ] M2-A1. Challenge phrases are single use, expire after the TTL, and are bound to user and action (tests).
- [ ] M2-A2. A step-up assertion is rejected when tampered, expired, replayed, issued to another user, or presented without a valid bearer token (tests).
- [ ] M2-A3. The analyst endpoints return 403 without the Analyst role and list score, result and reason per attempt.
- [ ] M2-A4. A verifier timeout or error returns the fallback instruction and never a pass (test).
- [ ] M2-A5. Deleting a voiceprint removes the embedding row and later step-up attempts get ReasonCode `NoVoiceprint` (test).
- [ ] M2-A6. Test and runtime configuration switch between Entra and the fake authority with no code change.
- [ ] M2-A7. After 5 consecutive failed voice attempts a user must wait at least 30 s before the next; after 50 voice is disabled for that user and the fallback is offered (tests; the limits are configurable).

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
