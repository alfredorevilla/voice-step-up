# Plan: voice-step-up, M7 Edge cases and end-to-end tests

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M7 Edge cases and end-to-end tests
**Complexity**: Medium
**Depends on**: M3, M4, M5, M6
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
Consolidate and gap-check the edge-case tests (each is written in the milestone that builds the behaviour), add Playwright end-to-end tests with a fake microphone, and run the quality audits.

## Patterns to Mirror
Mirror the code written in the earlier milestones; the plan is re-grounded before execution.

## Files to Change
| Path | Action | Why |
|---|---|---|
| tests/** | UPDATE | Edge-case integration tests |
| web/e2e/**, web/playwright.config.ts | CREATE | Playwright end-to-end tests |
| .github/workflows/ci.yml | UPDATE | E2E job |

## Tasks
- **T7.1** Gap-check the integration tests for each spec edge case: noisy audio, muted mic, replay (consumed challenge and old recording), synthetic voice (documented; measured if M6 produced a set), illness (borderline score gives fallback, not lockout; re-enroll after MFA), consent withdrawal and deletion, verifier timeout. Each test is written in the milestone that builds the behaviour (M2 to M4); this task adds the missing ones.
- **T7.2** Playwright E2E with `@playwright/test` (structure per `e2e-testing`; Chromium, fake audio device with a WAV, microphone permission) against the offline compose stack: enroll, pass, wrong phrase then fallback, muted, withdrawal.
- **T7.3** Quality pass: `test-anti-patterns`, `assertion-quality`, `csharp-reviewer`, `security-review`; fix findings.
- **T7.4** Coverage measured and reported (target 80% lines on Core and Api).

## Validation
```bash
dotnet test
(cd web && npx playwright test)
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| Headless Chromium may ignore the fake-microphone flag | Medium | New headless Chromium channel; spike at the start of T7.2 |
| Flaky end-to-end tests | Medium | Deterministic WAV input; no real network |

## Acceptance
- [x] M7-A1. Each of the six spec edge cases has at least one automated test that fails when the behaviour is removed.
- [x] M7-A2. E2E covers enroll, successful step-up, failed step-up with fallback, and consent withdrawal, and runs in CI.
- [x] M7-A3. Line coverage on Core and Api is at least 80%, or the gap is explained in the README.
- [x] M7-A4. The test-quality audit and security review have no open medium-or-higher findings.

## Evidence and decisions (2026-10-21)

- **M7-A1.** The six edge cases and the tests that fail when the behaviour is removed (mutation-checked; each break turned exactly its own test red):
  - noisy, muted, too-short audio: `EdgeCaseTests.Verify_BadAudio_IsNamedKeepsTheChallengeAndNeverCountsAsAFailure` (3 cases; breaks: a quality reject counted as a failure, a quality reject consuming the challenge) and the e2e muted test (break: the Muted message removed);
  - replay: `Verify_ReplayingAnOldRecordingAgainstANewChallenge_IsRefused` (break: phrase check removed) plus the existing consumed-challenge and replayed-assertion tests;
  - synthetic voice: a documented limit, no defence, and none measured. `DocumentedLimitsTests` fail if TM-002 or the evaluation report stop saying so, or an overclaim ("replay-proof", "provides liveness" and others) appears in the architecture or ADR-0002 (3 breaks, 3 reds);
  - illness: `Illness_BorderlineScore_OffersTheFallbackWithoutLockingVoice_AndReEnrollmentAfterMfaWorks` (breaks: score check removed, fallback not offered);
  - consent withdrawal and deletion: existing API tests plus the e2e withdrawal test (break: the DELETE call replaced);
  - verifier timeout: existing `Verify_FailsClosedOnTimeout` and the HTTP-mode tests (M2/M3).
- **M7-A2.** `web/e2e/stepup.spec.ts` runs five serial tests on the offline stack (fake authority, API in Demo mode with the Fake verifier, built SPA): enroll, voice step-up pass, wrong phrase then multi-factor fallback, muted microphone then retry, consent withdrawal. 39 s locally. The `e2e` job in `ci.yml` runs it; its first run on GitHub is pending a push (not pre-approved). Chromium's fake microphone supplies real captured audio, and the test asserts the upload is RIFF/WAVE PCM16 mono 16 kHz. Because the Fake verifier cannot score a tone and challenge phrases are random, the body is swapped for the Fake payload at the network layer just before it leaves the browser (a documented seam, `web/e2e/app.ts`). The 5 e2e breaks (sample rate, assertion header dropped, leaky failure message, muted message, DELETE as GET) each turned the matching test red.
- **M7-A3.** `scripts/coverage.sh` merges all test projects (Model-trait tests excluded): **StepUp.Core 343/424 = 80.9%, StepUp.Api 531/567 = 93.7%** lines. Core is close to the bar.
- **M7-A4.** Audit by the `test-anti-patterns` and `assertion-quality` checks plus a C# and security self-review (a fork cannot spawn reviewers). Scan of 264 test methods; findings fixed: `Assertion_Rejects…` and `Fallback_Rejects…` asserted only a boolean, so a rejection for the wrong reason would pass. Each now pins its own `Failure` or `Why` text. The remaining single-assert tests assert a message or a relationship, or are boolean decision functions with paired positive and negative cases. No open medium-or-higher finding. Not covered by an independent reviewer: a fresh `/code-review` pass is still open.
- **Product defect found by the first e2e run (fixed, `1ef3eb2`).** The SPA's fresh-multi-factor redirect carried no `login_hint`, so the fake authority signed the user in as its default identity (alice). Real Entra keeps the user through its session, so only the offline demo was affected. Fix: the PKCE client keeps the sign-in hint in `sessionStorage` for the browser session and drops it on sign-out (TM-016 row updated).
- **Local run note.** This machine exports `PLAYWRIGHT_BROWSERS_PATH` to a different folder and `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`; running the e2e locally needs `PLAYWRIGHT_BROWSERS_PATH=$HOME/.cache/ms-playwright` and `npx playwright install chromium` once (Playwright 1.64.1 wants Chromium build 1248).
- **Parameter independence.** No e2e or edge-case test hardcodes the phrase length, the phrase-match threshold or the speaker threshold: the illness test reads the seeded threshold through the analyst API, and the e2e tests read the phrase from the screen.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
