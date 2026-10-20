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
| README.md, NOTICE | UPDATE | Quickstart, limits, attributions |
| docs/threat-model.md | UPDATE | Refresh with code evidence anchors |
| docs/adr/* | UPDATE | Final statuses |
| compose.yaml, scripts/demo-entra.sh | UPDATE | One-command demo |
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
- [ ] M8-A1. On a clean clone, one command starts the offline demo and the health endpoints return healthy.
- [ ] M8-A2. The README contains the threat model summary, honest limits (including the NIST SP 800-63B-4 position on voice), privacy and consent statements, and the FAR/FRR results table.
- [ ] M8-A3. `docs/threat-model.md` covers replay, synthetic voice, enrollment abuse, threshold tampering, fallback downgrade, `amr` forgery, consent and deletion, and audio handling.
- [ ] M8-A4. All CI jobs are green on the pull request.
- [ ] M8-A5. `/init` and `/update-docs` have run and their output is committed.
- [ ] M8-A6. Every ADR 0001 to 0008 has status Accepted or Superseded and matches the implementation; none is still Proposed.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
