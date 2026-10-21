# ADR-0002: Replay resistance by random phrase plus ASR check

Status: Proposed (pending the ASR spike, M3 T3.1)
Date: 2026-10-20
Decisions: D2

## Context
A recorded clip of the user must not pass. A random phrase only defeats replay if the spoken content is checked. This is replay resistance against re-submitting a whole earlier recording. It is not liveness and not presentation attack detection (PAD).

## Decision
The server issues a single-use 4-word phrase from a CSPRNG, valid 60 s (configurable), bound to the user and action. A pass needs the ASR transcript to match the phrase and the speaker score to meet the threshold; both checks always run. ASR is the sherpa-onnx offline recognizer; Whisper tiny.en is the first candidate. T3.1 sets an accuracy gate (phrase-accept rate on recorded 4-word phrases, reject rate on wrong phrases), the word-error-rate rule and the text normalisation, and states the word-list size and phrase entropy.

## Alternatives considered
- Fixed passphrase: trivially replayable.
- Speaker score only: no replay resistance at all.
- A dedicated anti-spoofing model: not selected; cost and licence unknown.
- Digits only: shorter to read, less ASR-robust.
- Cut the TTL to 15-20 s (review proposal): rejected. Reading and recording a 4-word phrase needs the time, and a shorter window does not stop splicing or voice conversion. The limit is stated instead.

## Consequences
- Words spliced from recordings of the victim, and a cloned or converted voice that reads the phrase, can still pass; these are stated limits (threat model TM-001, TM-002). Enrollment phrases and past attempts are a word source for an attacker, so the challenge word list should not reuse enrollment words.
- Whisper can hallucinate on short or near-silent input; the spike must test that.
- The spike (T3.1) may force another ASR model or a looser match; this ADR stays Proposed until then.
