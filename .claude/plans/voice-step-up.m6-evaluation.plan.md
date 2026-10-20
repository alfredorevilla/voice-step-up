# Plan: voice-step-up, M6 FAR/FRR evaluation

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M6 FAR/FRR evaluation
**Complexity**: Medium
**Depends on**: M3
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
An evaluation tool that fetches LibriSpeech test-clean, generates seeded trials and degradations, computes FAR, FRR and EER with intervals, and publishes the report; default thresholds trace to it.

## Patterns to Mirror
Mirror the code written in the earlier milestones; the plan is re-grounded before execution.

## Files to Change
| Path | Action | Why |
|---|---|---|
| eval/StepUp.Eval | CREATE | Evaluation tool |
| docs/eval-report.md, docs/design/evaluation-protocol.md | CREATE | Report and pre-registered protocol |
| scripts/eval.sh, .github/workflows/eval.yml | CREATE | Reproduction and manual CI run |
| tests/StepUp.Eval.Tests | CREATE | Metric unit tests |

## Tasks
- **T6.1** `eval/StepUp.Eval`: fetch LibriSpeech `test-clean` from OpenSLR (md5 check) into a git-ignored folder; seeded trial lists; degradations (noise at set SNRs, 8 kHz band-limit, 2 s clips). Spoof set: TTS clips only if the TTS model licence is clean (checked first with Exa search and fetch of the model card); otherwise report spoofing as not measured. First write `docs/design/evaluation-protocol.md` fixing the trial design, conditions and thresholds before any result is seen.
- **T6.2** Metrics: FAR, FRR, EER, DET points, Wilson 95% intervals; cohort mean score normalization experiment; unit tests with hand-computed values. Results are reported next to the NIST SP 800-63B-4 targets (false match rate of 1 in 10,000 or better, false non-match rate under 5%) with the number of impostor trials that would be needed to support the first (zero errors in n trials bounds the rate at about 3/n with 95% confidence, so about 30,000 trials).
- **T6.3** Run and publish `docs/eval-report.md` plus CSV, with attribution and a "small test set" caveat; `scripts/eval.sh`; manual `eval.yml` workflow.
- **T6.4** Set default thresholds in configuration from the report.

## Validation
```bash
scripts/eval.sh
dotnet test tests/StepUp.Eval.Tests
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| Scores separate poorly, so thresholds look worse than expected | Medium | Cohort normalization experiment; report honestly |
| Dataset or TTS model licence is not clean | Low | Read the owner's page first (third-party-assets rule); otherwise report spoofing as not measured |
| The sample is too small to support the NIST false match rate | High | State it in the report (M6-A4) |

## Acceptance
- [ ] M6-A1. `scripts/eval.sh` reproduces the report from a clean checkout (dataset fetched, checksums verified).
- [ ] M6-A2. The report states FAR and FRR per threshold and per condition, with intervals and the number of trials.
- [ ] M6-A3. Metric calculations are unit-tested against hand-computed examples.
- [ ] M6-A4. The report states its limits: read speech, small speaker count, no real-world spoofing coverage, and that the sample is too small to demonstrate the NIST SP 800-63B-4 false match rate of 1 in 10,000.
- [ ] M6-A5. Default thresholds in config trace to the report.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
