# Plan: voice-step-up, M8 Docs, threat model refresh and one-command demo

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M8 Docs, threat model refresh and one-command demo
**Complexity**: Medium
**Depends on**: M7
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
Compose-based offline demo and the Entra demo script, the README with honest limits, the threat model refreshed with code evidence, a final check of every ADR and document against the code, and the closing CI and docs commands.

## Patterns to Mirror
Mirror the code written in the earlier milestones; the plan is re-grounded before execution.

## Files to Change
| Path | Action | Why |
|---|---|---|
| README.md, NOTICE, CLAUDE.md | UPDATE, CREATE | Quickstart, results, limits, attributions; guidance for future sessions |
| docs/threat-model.md | UPDATE | Refresh with code evidence anchors |
| docs/adr/* | UPDATE | Final statuses |
| compose.yaml, Dockerfile, web/Dockerfile, scripts/demo-entra.sh, scripts/demo-smoke.mjs | UPDATE, CREATE | One-command demo, real-Entra demo, sign-in smoke test |
| src/StepUp.FakeAuthority | UPDATE | `FakeAuthority:PublicUrl` pins the issuer so the API (service name) and the browser (localhost) share keys |
| .github/workflows/ci.yml | UPDATE | All jobs |

## Tasks
- **T8.1** `docker compose up --build` (offline mode, fake authority) and `scripts/demo-entra.sh` (real Entra); README quickstart; health checks. Validate on a clean clone.
- **T8.2** Refresh `docs/threat-model.md` by running `security-threat-model` interactively against the code, so every claim has a code evidence anchor, and link it from the README. Compare with the design-level threats from T1.3 and note what changed.
- **T8.3** Final document check: set every ADR to Accepted or Superseded and verify each ADR and `docs/architecture.md` against the code; README: architecture, privacy and consent, data retention, honest limits (including that NIST SP 800-63B-4 says voice comparison SHALL NOT be used, that this project verifies centrally rather than on the user's device, and that it is a demonstrator, not a conformant authenticator), attributions (`NOTICE`).
- **T8.4** CI complete: build, test, web, Bicep, E2E, actionlint, compose config.
- **T8.5** After implementation: `/init` and `/update-docs`.

## Validation
```bash
docker compose up --build --wait
gh run list --workflow ci.yml --limit 1
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| The project cannot conform to NIST SP 800-63B-4 (voice comparison SHALL NOT be used; the false match rate target needs a far larger test set; comparison is central) | High | Say so in the README and threat model; keep the project labelled a demonstrator |
| Documents drift from the code | Medium | T8.3 check of every ADR and doc |

## Acceptance
- [x] M8-A1. On a clean clone, one command starts the offline demo and the health endpoints return healthy. (2026-10-21: `git clone` of the committed HEAD, `docker compose up --build --wait` built the models into the image, all three services healthy, `scripts/demo-smoke.mjs` passed health, a PKCE sign-in and an authenticated API call, then `docker compose down -v`. Built with an isolated buildx builder because this machine's default builder cannot run `dotnet` in a build.)
- [x] M8-A2. The README contains the threat model summary, honest limits (including the NIST SP 800-63B-4 position on voice), privacy and consent statements, and the FAR/FRR results table.
- [x] M8-A3. `docs/threat-model.md` covers replay, synthetic voice, enrollment abuse, threshold tampering, fallback downgrade, `amr` forgery, consent and deletion, and audio handling.
- [x] M8-A4. All CI jobs are green on the pull request. (Evidence: all six jobs, actionlint, bicep, build-test, demo, e2e and web, are green on PR #1, run https://github.com/alfredorevilla/voice-step-up/actions/runs/39668842733.)
- [x] M8-A5. `/init` and `/update-docs` have run and their output is committed.
- [x] M8-A6. Every ADR 0001 to 0008 has status Accepted or Superseded and matches the implementation; none is still Proposed. (Checked against the code 2026-10-21: corrected the `ModelId` definition, the assertion consume point, the fake-mode guard wording and the compose description; ADR-0002 and ADR-0004 evidence read before accepting.)

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.

## Status and decisions (2026-10-21)

Done by the M8 fork on `feat/voice-step-up`: T8.1 to T8.5. Open: the blockers below.

- **Decision: the compose demo runs the real verifier.** The Fake verifier ignores speech and reads a `[quality][speaker][phrase]` payload from the WAV samples, so a microphone cannot use it (enrollment samples would look like different speakers and every phrase would mismatch). A demo that is only "healthy" would overstate. The default services are therefore the fake authority, the API with sherpa-onnx and the SPA, and the image fetches the models at build time. The Fake verifier stays for tests and the browser e2e. Rejected: a `--profile fast` Fake-verifier variant (dead code for an interactive demo) and an SPA stand-in audio button (scope). Decided by the fork alone: a fork cannot convene `/council` or `/teammates`, so the parent should review this record.
- **Decision: `FakeAuthority:PublicUrl`** pins the issuer, because the fake authority built its signing key per request host, so a token minted for `localhost:5100` failed against keys fetched as `authority:5100`. Test: `PublicUrl_PinsTheIssuerWhateverHostNameTheCallerUsed`.
- **Not written:** `docs/CONTRIBUTING.md` and `docs/RUNBOOK.md` (a demonstrator with no deployed service; README and CLAUDE.md cover setup and commands).
- **Blocker for the push:** the tenant id and subscription id are in git history (commits `f7da6dc` and `ee3b639`, removed from the tree since). The repository is public, so the history must be rewritten before any push. Not done: it needs the user's decision.
- **Open (not M8):** none. M5-A2 closed 2026-10-21; the Entra apps remain in the tenant until `scripts/entra-teardown.sh --yes` is run.
- **Environment gotcha:** the default Docker builder here runs `dotnet` as a silent no-op inside a build; use `docker buildx create --name stepup-bk --driver docker-container` and `BUILDX_BUILDER=stepup-bk`. CI is unaffected.
