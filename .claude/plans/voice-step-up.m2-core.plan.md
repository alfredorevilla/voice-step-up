# Plan: voice-step-up, M2 Core domain, policy and API (fake verifier)

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M2 Core domain, policy and API (fake verifier)
**Complexity**: Medium
**Depends on**: M0, M1
**Plan status**: executed 2026-10-21. Patterns to Mirror and Files to Change were re-grounded against the M0 code before starting.

## Summary
The service without real audio: domain interfaces, challenge and assertion logic, persistence, the API, a fake OIDC authority and a shared IVerifier contract test suite.

## Patterns to Mirror
Re-grounded on 2026-10-21 against the M0 code (a `StepUp.Api` health endpoint, `StepUp.Core` and `StepUp.Infrastructure` with only marker types, xUnit v3 test projects). Conventions to follow are in `Directory.Build.props` (nullable, warnings as errors, latest-recommended analyzers), `.editorconfig` (tests relax CA1707 and CA1861), `tests/Directory.Build.props` (global `Xunit` using) and the lazy rules in `.claude/lazy/rules/`. The pattern M2 sets for later milestones: Core holds pure logic and interfaces, Infrastructure holds EF Core stores, Api holds endpoints and flow, tests use real SQLite in a temp directory and `WebApplicationFactory` over HTTP.

## Files to Change
| Path | Action | Why |
|---|---|---|
| src/StepUp.Core (types, interfaces, challenge service, assertion service, fallback, lockout, WAV check, cosine) | CREATE | Pure domain logic |
| src/StepUp.Infrastructure (EF Core stores, Data Protection, key files, `FakeVerifier` and `FakeAudio`) | CREATE | Persistence and the demo verifier |
| src/StepUp.Api (auth modes, endpoints, `VerificationService`, `StepUpFilter`) | CREATE | HTTP surface |
| src/StepUp.FakeAuthority | CREATE | Offline OIDC authority and token minter |
| tests/StepUp.Core.Tests, tests/StepUp.Infrastructure.Tests, tests/StepUp.Api.Tests, tests/StepUp.Verifier.Tests (contract suite) | CREATE | xUnit v3 suites |
| Directory.Packages.props, VoiceStepUp.slnx, .gitignore | MODIFY | Packages (with a pinned patched `System.Security.Cryptography.Pkcs`), projects, `data/` ignored |

## Tasks
- **T2.1** Interfaces and types in `StepUp.Core`: `IVerifier`, `IChallengeService`, `IStepUpPolicy`, `IFallbackFactor`, `IVoiceprintStore`, `IAttemptLog`, `IConsentStore`, `IThresholdStore`; reason codes (Muted, TooShort, Noisy (also covers clipping), PhraseMismatch, BelowThreshold, Timeout, VerifierError, NoVoiceprint, ConsentMissing, ChallengeExpired, ChallengeConsumed, RateLimited, StepUpRequired, FreshMfaRequired, ReEnrollRequired, VerificationFailed). `IVerifier` returns the probe embedding and transcript; the Api scores. `ModelId` as defined in `docs/architecture.md` section 3.
- **T2.2** Challenge service (4-word CSPRNG phrase, single use, 60 s TTL configurable, bound to user and action; the phrase is stored in plain form server-side and nulled on consume or expiry; open challenges per user capped). Tests include `Challenge_RejectsOtherUser`, `Challenge_RejectsOtherAction`, `Challenge_ConcurrentVerify_OnlyOneSucceeds`. TDD via `tdd-workflow`.
- **T2.3** Step-up assertion issuer/validator (ADR-0005): ES256, `typ` `stepup+jwt`, `aud` own audience, `act`, optional `ph`, single use via `UsedAssertions`, own key set, key generated at first start under Data Protection. Tests: tamper, expiry, wrong user, replay, missing bearer, `Assertion_RejectsWrongAction`, `Assertion_RejectsChangedPayload`, `Assertion_IsSingleUse`, `Assertion_RejectsAlgNone`, `Assertion_RejectsHs256`, `Assertion_RejectsUnknownKid`, `Assertion_RejectsEntraToken`, `Bearer_RejectsStepUpAssertion`.
- **T2.4** EF Core SQLite persistence with the tables of `docs/architecture.md` section 3 (including `Enrollments`, `UsedAssertions`, `ModelId` on `Thresholds` and `Attempts`), `UserKey` as HMAC, `PRAGMA secure_delete = ON`, Data Protection encryption of embeddings, deletion path, append-only `Attempts`. Test `Delete_LeavesNoEmbeddingBytesInDbFile`.
- **T2.5** API (`dotnet-webapi`; `Microsoft.Identity.Web` usage checked against Microsoft Learn MCP): `GET /api/me`; enrollment (`consent`, `start`, `sample`, `complete`); `DELETE /api/voiceprint`; `POST /api/stepup/challenge`, `POST /api/stepup/verify`; analyst `GET /api/analyst/attempts`, `GET|PUT /api/analyst/thresholds`; demo high-risk `POST /api/payouts`; `Auth:Mode` = Entra (`AddMicrosoftIdentityWebApi`, single tenant GUID, `Audience` = App ID URI, `RequiredScope`, `scp` and `roles` checks, reject app-only tokens) or Fake (`AddJwtBearer`, same `TokenValidationParameters` shape); `MapInboundClaims = false` in both; Fake mode starts only with an explicit demo flag in Development or on a loopback or compose network, and Entra mode never trusts the fake issuer; enrollment `start`/`complete` need fresh MFA; `/api/payouts` passes on a valid assertion OR `IFallbackFactor` fresh MFA (`amr` `mfa`/`ngcmfa` and `auth_time` within N minutes, default 10, missing `auth_time` fails closed), logged as `Mode=fallback`; one generic `VerificationFailed` to the client, score and threshold never returned (`Verify_FailResponse_IsUniform`); CORS allowlist of the SPA origin (`Cors_RejectsOtherOrigins`); per-user rate limits on `sample`, `challenge`, `verify`, a global verifier concurrency limit; `[RequiresStepUp]` policy; attempt limiting (after 5 consecutive failed voice attempts a delay of at least 30 s; after 50 voice is disabled for that user and the fallback is offered; these are the NIST SP 800-63B-4 section 3.2.3.3 limits for a system without presentation attack detection, configurable) plus general rate limiting; fail-closed verifier timeout (default 3 s) returning the fallback instruction.
- **T2.6** `StepUp.FakeAuthority`: discovery, JWKS, authorize/token for the SPA, and a token-mint helper for tests (claims `oid`, `tid`, `amr`, `auth_time`, `roles`). Supports PKCE and CORS on its token endpoint; its authorize endpoint honors a `claims` parameter asking for `ngcmfa` by issuing `amr` that includes `ngcmfa` and a fresh `auth_time`. Signing key generated per run.
- **T2.7** Abstract `IVerifier` contract test suite, run against a `FakeVerifier`.

## Decisions made during M2
A fork ran this milestone and could not convene a panel; each decision below is recorded for the next review.
- Payout assertions are bound to `amount` and `to` at verify time (`ph`); a mismatching presentation is refused without consuming the assertion.
- A deleted voiceprint answers `NoVoiceprint` before `ConsentMissing`, because `DELETE /api/voiceprint` also withdraws consent.
- Entra mode adds a `tid` equals configured-tenant check to the user policy on top of Identity.Web's issuer validation, and refuses to start unless `AzureAd:TenantId` is a GUID, so the check cannot switch itself off (review fix 3).
- The Entra-mode unit tests stub Identity.Web's issuer validator (it fetches Entra metadata over the network); the real validator is exercised in M5 T5.4.
- Test configuration uses `UseSetting`, not `AddInMemoryCollection`, because `Program.cs` reads configuration before the factory's overrides apply.
- A patched `System.Security.Cryptography.Pkcs` is pinned centrally (transitive pinning) rather than suppressing NU1902 and NU1903.
- Transport: audio is the raw `audio/wav` body with identifiers in the query string, which avoids multipart and antiforgery handling.
- Known limits: expired challenges and used assertions are not purged yet (enrollments are: on read and on every `start`); parallel verifications can overrun the lockout by at most the open-challenge cap (3); the Data Protection key ring and the signing key share a directory without OS-level encryption.

## Review fix turn 1 (independent `/code-review`, 7 medium findings, all applied)
1. Consent is checked in `sample` and `complete` (`ConsentMissing`); withdrawal deletes the user's in-progress enrollments; expired enrollments are deleted on read and on every `start`.
2. Withdrawal re-keys the user's attempts to a random token per row (`IAttemptLog.PseudonymiseAsync`), the single sanctioned exception to append-only; scores stay for the analyst.
3. Entra mode refuses to start without a GUID `AzureAd:TenantId`; `tid` is always enforced.
4. Every refusal is logged as `Refused` with its reason (no voiceprint, no consent, locked out, expired or used challenge, missing or bad step-up); a failed assertion gets `AssertionReplayed` or `AssertionInvalid` in the log only. `Refused` never feeds the lockout counter.
5. A successful fallback resets the lockout counter, matching architecture section 4 and TM-012; the store test now asserts it.
6. Enrollment phrases use a separate word list that shares no word with the challenge list (test).
7. The HS256, unknown-kid and Entra-token assertion tests now fail only on their own control. Checked by removing each control (`ValidAlgorithms`, `TryAllIssuerSigningKeys`, `ValidTypes`, `ValidateAudience`, `ValidateIssuer`) and watching the matching test go red. The uniformity test compares the two failure bodies; an Entra-mode variant of `Bearer_RejectsStepUpAssertion` exists.

## Validation
```bash
dotnet build && dotnet test   # via run-tests
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| Assertion design allows replay or cross-user use | Medium | M2-A2 tests: tamper, expiry, replay, other user, missing bearer |
| Rate and attempt limiting is wrong | Medium | M2-A7 tests with configurable limits |
| Fake mode reaches a real deployment, or the fallback trusts stale MFA | Medium | M2-A8 and M2-A9 tests |

## Acceptance
- [x] M2-A1. Challenge phrases are single use, expire after the TTL, and are bound to user and action (tests). Evidence: Core `Challenge_*` tests, API `Challenge_IsSingleUse_OverHttp`, `Challenge_ExpiresAfterTheTtl_OverHttp`, `Challenge_RejectsAnotherUsersChallengeAndAnotherAction`; real-store `Challenge_ConcurrentConsume_OnlyOneSucceeds_OnTheRealStore`.
- [x] M2-A2. A step-up assertion is rejected when tampered, expired, replayed, issued to another user, or presented without a valid bearer token (tests). Evidence: Core `Assertion_*` (tamper, expired, wrong user, replay, alg none, HS256, unknown kid, Entra token), API `Payout_*` (replay, other user, no bearer, changed payload), `Bearer_RejectsStepUpAssertion`.
- [x] M2-A3. The analyst endpoints return 403 without the Analyst role and list score, result and reason per attempt. Evidence: `Analyst_EndpointsRejectUserRole`, `Analyst_SeesScoreResultAndReasonPerAttempt`.
- [x] M2-A4. A verifier timeout or error returns the fallback instruction and never a pass (test). Evidence: `Verify_FailsClosedOnVerifierError`, `Verify_FailsClosedOnTimeout`.
- [x] M2-A5. Deleting a voiceprint removes the embedding row and later step-up attempts get ReasonCode `NoVoiceprint` (test). Evidence: `Delete_LeavesNoEmbeddingBytesInDbFile`, `Verify_AfterDelete_ReturnsNoVoiceprint`.
- [x] M2-A6. Test and runtime configuration switch between Entra and the fake authority with no code change. Evidence: The whole API suite runs in Fake mode; `EntraMode_*` tests run the same pipeline in Entra mode; the switch is `Auth:Mode`.
- [x] M2-A7. After 5 consecutive failed voice attempts a user must wait at least 30 s before the next; after 50 voice is disabled for that user and the fallback is offered (tests; the limits are configurable). Evidence: `Attempts_DelayAfter5_AndDisableAfter50`, `Attempts_ResetOnSuccess`, `Attempts_QualityRejectsDoNotCountTowardTheLimit`, Core `Attempts_*`.
- [x] M2-A8. Enrollment start and complete are refused without fresh MFA (`FreshMfaRequired`); the fallback is accepted only with `amr` `mfa`/`ngcmfa` and a recent `auth_time`, and rejected when stale, missing or without MFA (tests: `Enroll_RequiresFreshMfa`, `Fallback_AcceptsFreshMfa`, `Fallback_RejectsStaleAuthTime`, `Fallback_RejectsMissingAuthTime`, `Fallback_RejectsAmrWithoutMfa`). Evidence: `Enroll_RequiresFreshMfa`, `Fallback_AcceptsFreshMfa`, `Fallback_RejectsStaleAuthTime`, `Fallback_RejectsMissingAuthTime`, `Fallback_RejectsAmrWithoutMfa` (API and Core).
- [x] M2-A9. Fake mode is refused outside a demo and Entra mode rejects the fake issuer; an assertion is rejected for another action, a changed payload, a reuse, a bad algorithm or unknown `kid` (tests: `Startup_RefusesFakeModeOutsideDemo`, `EntraMode_RejectsFakeIssuerToken`, the T2.3 assertion tests). Evidence: `Startup_RefusesFakeModeOutsideDemo`, `EntraMode_RejectsFakeIssuerToken`, `Assertion_RejectsWrongAction`, `Assertion_RejectsChangedPayload`, `Assertion_IsSingleUse`, `Assertion_RejectsAlgNone`, `Assertion_RejectsHs256`, `Assertion_RejectsUnknownKid`.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
