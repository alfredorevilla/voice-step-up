# ADR-0002: Liveness by random phrase plus ASR check

Status: Proposed (pending the ASR spike, M3 T3.1)
Date: 2026-10-20
Decisions: D2

## Context
A recorded clip of the user must not pass. A random phrase only defeats replay if the spoken content is checked.

## Decision
The server issues a single-use 4-word phrase from a CSPRNG, valid 60 s, bound to the user and action. A pass needs the ASR transcript to match the phrase and the speaker score to meet the threshold. ASR is the sherpa-onnx offline recognizer; Whisper tiny.en is the first candidate.

## Alternatives considered
- Fixed passphrase: trivially replayable.
- Speaker score only: no liveness at all.
- A dedicated anti-spoofing model: not selected; cost and licence unknown.
- Challenge by digits only: shorter to read, less ASR-robust.

## Consequences
- A good text-to-speech clone that reads the phrase can still pass; stated as a limit.
- The spike (T3.1) may force another ASR model or a looser match; this ADR stays Proposed until then.
