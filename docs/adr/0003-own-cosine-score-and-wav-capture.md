# ADR-0003: Own cosine score and browser WAV capture

Status: Proposed
Date: 2026-10-20
Decisions: D3, D4

## Context
The analyst needs a numeric score per attempt. sherpa-onnx `SpeakerEmbeddingManager.Verify` returns only a boolean. The server should not decode browser codecs.

## Decision
- The service computes cosine similarity from the enrolled and probe embeddings.
- The browser captures PCM with an AudioWorklet and uploads 16 kHz mono PCM16 WAV (max 10 s, max 1 MB).

## Alternatives considered
- Use `Verify` and log only pass or fail: no score for threshold tuning.
- Upload WebM/Opus from `MediaRecorder` and decode with ffmpeg: adds a native dependency and a decoding attack surface.
- Resample on the server: spreads audio handling across two places.

## Consequences
- Thresholds are in cosine units and tied to the model id.
- The SPA owns resampling; a bad worklet gives bad audio, caught by the quality gates.
