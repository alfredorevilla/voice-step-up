# ADR-0003: Own cosine score and browser WAV capture

Status: Accepted
Date: 2026-10-20
Decisions: D3, D4

## Context
The analyst needs a numeric score per attempt. sherpa-onnx `SpeakerEmbeddingManager.Verify` returns only a boolean. The server should not decode browser codecs.

## Decision
- The service computes cosine similarity from the enrolled and probe embeddings. The verifier returns the probe embedding; the stored embedding never leaves the Api.
- `ModelId` = SHA-256 of `models/models.lock` + sherpa-onnx version + feature and normalisation config version. Thresholds and attempts are keyed by `ModelId`. A stored voiceprint with another `ModelId` fails closed with `ReEnrollRequired` and the fallback is offered.
- Enrollment: each sample embedding is L2-normalised, averaged, and normalised again; samples whose pairwise cosine is below a consistency floor are rejected. M6 Amendment 2 set the floor at 0.43 for the 3.5 s enrollments of 6-word phrases (rules R3 and R6, dev-clean); the first report's 0.46 floor on 2.5 s enrollments caught 47.5% of poisoned enrollments (one sample from another speaker) on test-clean, so the check limits poisoning and does not stop it. The 0.43 floor has no poisoned-enrollment measurement.
- The browser captures PCM with an AudioWorklet and uploads 16 kHz mono PCM16 WAV (max 10 s, max 1 MB). `getUserMedia` constraints `echoCancellation`, `noiseSuppression` and `autoGainControl` are all false, and the resampler low-pass filters (sine-sweep unit test).
- Scores are cosine values, never called calibrated. Cohort normalisation is adopted only if the M6 protocol says so; then a cohort-set id joins `ModelId` and `Attempts`.

## Alternatives considered
- Use `Verify` and log only pass or fail: no score for threshold tuning.
- Upload WebM/Opus from `MediaRecorder` and decode with ffmpeg: adds a native dependency and a decoding attack surface.
- Resample on the server: spreads audio handling across two places.
- Leave the browser's default audio processing on: it changes the spectrum differently per browser.

## Consequences
- Thresholds are in cosine units and tied to the model id.
- The SPA owns resampling; a bad worklet gives bad audio, caught by the quality gates. Channel and browser mismatch with the evaluation data is a stated limit.
