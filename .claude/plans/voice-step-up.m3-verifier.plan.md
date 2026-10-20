# Plan: voice-step-up, M3 Real verifier and audio pipeline

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M3 Real verifier and audio pipeline
**Complexity**: Large
**Depends on**: M2
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
The sherpa-onnx verifier, the ASR phrase check, WAV ingest and quality gates, model fetching with checksums, the container image, and an HttpVerifier adapter.

## Patterns to Mirror
Mirror the code written in the earlier milestones; the plan is re-grounded before execution.

## Files to Change
| Path | Action | Why |
|---|---|---|
| src/StepUp.Verifier.Sherpa | CREATE | SherpaVerifier, ingest, phrase check |
| scripts/fetch-models.sh, models/models.lock | CREATE | Checksum-pinned model download |
| Dockerfile, compose.yaml (api service) | CREATE | Container and healthcheck |
| tests/StepUp.Verifier.Tests | UPDATE | Contract suite for the new verifiers |

## Tasks
- **T3.1** Spike with go/no-go: sherpa ASR (Whisper tiny.en first) checksum-verified, latency on 20 short clips. Record the outcome in ADR-0002. If p95 latency is above 3 s, choose a smaller model or stop and ask. After the spike, write `docs/design/verifier.md` (ingest limits, quality gates and reason codes, how enrollment samples are combined, phrase-check thresholds, concurrency) before T3.3, and set ADR-0002 to Accepted or Changed.
- **T3.2** WAV ingest and validation (PCM16 mono 16 kHz, max 10 s, max 1 MB) and quality gates (too short, muted, clipped, low SNR) with reason codes; synthetic-signal tests.
- **T3.3** `SherpaVerifier` (sherpa-onnx C# API checked with Context7): embedding extraction, own cosine, enrollment as the normalized mean of 3 samples, model id stored, thresholds from `IThresholdStore`, timeout via cancellation.
- **T3.4** Phrase check: sherpa offline recognizer plus word error rate against the challenge phrase.
- **T3.5** Concurrency and threads: one extractor instance, bounded concurrent verifications, configurable thread count.
- **T3.6** `scripts/fetch-models.sh` with `models/models.lock` SHA-256 check; `Dockerfile` (multi-stage, non-root, Debian-based ASP.NET image) and compose healthcheck (`docker-patterns`). Validate: the native library loads inside the container.
- **T3.7** `HttpVerifier` adapter against a stub verifier service, run through the same contract suite (story 3).

## Validation
```bash
scripts/fetch-models.sh && dotnet test   # Category=Model tests need the models
docker build . && docker compose up --wait
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| ASR phrase check too slow or inaccurate on short random phrases | Medium | T3.1 go/no-go spike; phonetically distinct word list; a failed phrase check is a fail that falls back, never a lockout |
| Native sherpa library does not load in the container | Medium | Debian-based ASP.NET image (not Alpine); load test in T3.6 |
| Raw cosine scores separate poorly for some speakers | Medium | Calibrated thresholds and cohort normalization from M6 |

## Acceptance
- [ ] M3-A1. The model download is checksum-verified and the verifier runs on CPU in WSL2 and in the container; measured latency per verification is recorded.
- [ ] M3-A2. Muted, too short, clipped and noisy inputs are rejected with the matching reason code (tests).
- [ ] M3-A3. A recording of one phrase submitted against a different challenge fails with `PhraseMismatch` (test).
- [ ] M3-A4. `SherpaVerifier` and `HttpVerifier` both pass the shared `IVerifier` contract suite.
- [ ] M3-A5. No raw audio is written to disk or logs during enrollment or verification (test that scans the data directory and captured logs).

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
