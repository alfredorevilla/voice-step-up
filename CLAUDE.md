# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Voice step-up for Microsoft Entra ID: a demonstrator, not a conformant authenticator (NIST SP 800-63B-4 says voice comparison SHALL NOT be used). Read `README.md` for the claims and limits, and `docs/design/honest-claims.md` before writing any claim about the project.

## Commands

- Build: `dotnet build` (warnings are errors; SDK pinned in `global.json`).
- Tests without models: `dotnet test -- --filter-not-trait "Category=Model"`. xUnit v3 on Microsoft.Testing.Platform, so filters go after `--`.
- One project or one test: `dotnet test --project tests/StepUp.Api.Tests -- --filter-method '*Name*'`.
- Model tests (real sherpa-onnx): `scripts/fetch-models.sh` (about 450 MB, checked against `models/models.lock`), then `dotnet test -- --filter-trait "Category=Model"`.
- SPA, in `web/`: `npm ci`, `npm test`, `npm run lint`, `npm run typecheck`; one file: `npx vitest run src/csp.test.ts`.
- Browser journey, in `web/`: `npx playwright test` (starts the fake authority, the API with the Fake verifier and `vite preview` itself). If `PLAYWRIGHT_BROWSERS_PATH` points elsewhere, set it to `~/.cache/ms-playwright`.
- Demo: `docker compose up --build --wait`, then `node scripts/demo-smoke.mjs`; `docker compose down -v` to stop.
- Evaluation: `scripts/eval.sh` (hours of CPU; uses LibriSpeech fetched at run time). Reproduces `docs/eval-report.md`.
- Real Entra: `scripts/entra-deploy.sh`, `scripts/demo-entra.sh`, `scripts/entra-teardown.sh`. They read the git-ignored `.env.local`; tenant and client ids must never appear in tracked files (CI scans for them).

## Architecture

`src/StepUp.Core` holds the domain with no I/O: the `IVerifier` seam, challenge service, `VoiceLockout`, `FreshMfaFallback`, `StepUpAssertion` (ES256 JWT), phrase check, cosine scoring. `src/StepUp.Infrastructure` has the SQLite/EF stores, Data Protection key handling, `FakeVerifier` and `HttpVerifier`. `src/StepUp.Verifier.Sherpa` is the in-process real verifier. `src/StepUp.Api` wires auth, endpoints (`Endpoints/StepUpEndpoints.cs`), and the verify flow (`Flow/VerificationService.cs`, `Flow/StepUpFilter.cs`). `src/StepUp.FakeAuthority` is an offline OIDC authority. `web/` is the React/Vite SPA. `eval/StepUp.Eval` measures false accept and false reject rates with the production code.

Things that need several files to see:

- The verifier returns an embedding and a transcript; the Api computes the cosine score and applies the stored threshold, so the stored voiceprint never leaves the Api. Both the phrase and the score checks always run and a failure is one generic `VerificationFailed`.
- A step-up pass is an assertion bound to user, action and (for payouts) payload, valid with a bearer token, consumed once. `[RequiresStepUp]` endpoints also accept fresh Entra MFA (`amr` `mfa`/`ngcmfa` plus `auth_time` within the window, never `iat`). Every verifier error fails closed to that fallback.
- `FakeVerifier` ignores speech and reads a payload from the WAV samples (`FakeAudio`). It is for tests and the e2e only. The compose demo uses the real verifier.
- Challenge length, threshold and consistency floor come from the evaluation: 6 words, 0.80 and 0.43 (`docs/eval-report.md`). They belong to a probe of about 3 s and to the model id.
- Fake sign-in and the Fake verifier start only with `Demo:Enabled=true` in the Development or Demo environment (`FakeModeGuard`).

## Working here

- Project rules are in `.claude/lazy/rules/` (biometric data, step-up invariants, tenant hygiene, C# and test conventions). Plans, the PRD and the loop runbook are in `.claude/plans/` and `.claude/prds/`.
- Tests use xUnit's built-in `Assert` only (no fluent assertion library) and go through HTTP with `WebApplicationFactory` for the API.
- Evidence for every security claim is in `docs/threat-model.md` (code anchors and test names); update it when a control changes.
- On this machine the default Docker builder cannot run `dotnet` inside a build (output is empty and no files are produced). Use an isolated builder: `docker buildx create --name stepup-bk --driver docker-container` and `BUILDX_BUILDER=stepup-bk docker compose up --build --wait`.
