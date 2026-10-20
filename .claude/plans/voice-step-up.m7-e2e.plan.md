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
- [ ] M7-A1. Each of the six spec edge cases has at least one automated test that fails when the behaviour is removed.
- [ ] M7-A2. E2E covers enroll, successful step-up, failed step-up with fallback, and consent withdrawal, and runs in CI.
- [ ] M7-A3. Line coverage on Core and Api is at least 80%, or the gap is explained in the README.
- [ ] M7-A4. The test-quality audit and security review have no open medium-or-higher findings.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
