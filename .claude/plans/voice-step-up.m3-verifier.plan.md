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
- **T3.1** Spike with go/no-go: sherpa ASR (Whisper tiny.en first) checksum-verified, latency on 20 short clips (p95 of embedding plus ASR together, inside the 3 s timeout) and an accuracy gate: phrase-accept rate on recorded 4-word phrases and reject rate on wrong phrases (test that Whisper does not hallucinate on short or near-silent input). Fix the word-error-rate rule and text normalisation (case, numbers, punctuation, word order). State the challenge word-list size and phrase entropy, and keep enrollment words out of the challenge list. Record the outcome in ADR-0002. If p95 latency is above 3 s, choose a smaller model or stop and ask. After the spike, write `docs/design/verifier.md` (ingest limits, quality gates with numeric values (peak and RMS floor, clipping ratio, minimum net speech after voice activity detection, the SNR method; clipping maps to `Noisy`; a quality reject does not consume the challenge), how enrollment samples are combined (L2-normalise each, mean, normalise again; pairwise consistency floor from M6; ASR check of enrollment phrases), phrase-check thresholds, concurrency) before T3.3, and set ADR-0002 to Accepted or Changed.
- **T3.2** WAV ingest and validation (PCM16 mono 16 kHz, max 10 s, max 1 MB) and quality gates (too short, muted, clipped, low SNR) with reason codes; synthetic-signal tests.
- **T3.3** `SherpaVerifier` (sherpa-onnx C# API checked with Context7): embedding extraction, own cosine, enrollment as the normalized mean of 3 samples (configurable), `ModelId` as defined in `docs/architecture.md` section 3 stored with the voiceprint (mismatch returns `ReEnrollRequired`), the verifier returns the probe embedding and the Api scores, thresholds from `IThresholdStore`, timeout via cancellation.
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
| Raw cosine scores separate poorly for some speakers | Medium | Thresholds and, only if the M6 protocol adopts it, cohort normalization from M6 (scores are never called calibrated) |

## Acceptance
- [x] M3-A1. The model download is checksum-verified and the verifier runs on CPU in WSL2 and in the container; measured latency per verification is recorded.
- [x] M3-A2. Muted, too short, clipped and noisy inputs are rejected with the matching reason code (tests).
- [x] M3-A3. A recording of one phrase submitted against a different challenge fails with `PhraseMismatch` (test).
- [x] M3-A4. `SherpaVerifier` and `HttpVerifier` both pass the shared `IVerifier` contract suite.
- [x] M3-A5. No raw audio is written to disk or logs during enrollment or verification (test that scans the data directory and captured logs).

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.

## M3 evidence and decisions (2026-10-21)
- **M3-A1**: `scripts/fetch-models.sh` checks every SHA-256 in `models/models.lock` (fresh fetch, re-run and a tampered file were each tried); the verifier checks the same file hashes at startup. Latency per verification, embedding plus ASR on 40 phrases: host p50 319 ms, p95 411 ms; container p50 322 ms, p95 424 ms (`docs/design/verifier.md` section 9). The Model suite also passes inside a container, and the `voice-step-up-api` image starts non-root and reports healthy through `docker compose up --wait`.
- **M3-A2**: `AudioQualityTests` (synthetic signals: silence and a quiet clip are `Muted`, short speech is `TooShort`, clipped and loud noise are `Noisy`); the clipped and loud-noise cases also pass through `SherpaVerifier` on the real models.
- **M3-A3**: `Analyze_APhraseSpokenAgainstAnotherChallenge_DoesNotMatch` (verifier) and `RealModels_EnrollStepUpAndWrongPhrase_ThroughTheApi_...` (the Api logs `PhraseMismatch`, the client sees `VerificationFailed`).
- **M3-A4**: `SherpaVerifierContractTests` and `HttpVerifierContractTests` derive from `VerifierContract`, as does the fake.
- **M3-A5**: `Analyze_WritesNoAudioToDiskAndNoNewFilesAnywhereItCouldPutThem` and, through the Api, a scan of the data directory (no `.wav`, `.pcm`, `.raw`, no RIFF header in any file) and of the captured logs.
- **T3.1 outcome**: go. Whisper base.en, letter-similarity match at 0.60 (ADR-0002, now Accepted).
- **Deviations from the plan text**: (1) The runtime image is the official `aspnet:10.0`, which is Ubuntu 24.04 (glibc), because no Debian-tagged 10.0 ASP.NET image exists; the plan's concern was glibc, not Alpine. (2) The plan has `SherpaVerifier` combine the three enrollment samples; the Api already does that (M2), so the verifier only returns embeddings. (3) The design doc is at `docs/design/verifier.md`. (4) Test speech is committed synthetic WAV fixtures plus a gated generator, because the text-to-speech differs on every run. (5) `ModelId` hashes only the speaker model, so a change of ASR model does not force re-enrollment.
- **Known limits**: speech accuracy is measured on synthetic voices only; the 0.60 rule tolerates about one wrong word of four; the native calls cannot be cancelled mid-flight (the token is checked between steps, the Api's 3 s timeout covers the rest); `dotnet` printed nothing inside `docker build` with this machine's default BuildKit builder, a `docker-container` builder worked.
