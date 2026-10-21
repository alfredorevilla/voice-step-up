# ADR-0002: Replay resistance by random phrase plus ASR check

Status: Accepted (ASR spike T3.1 done 2026-10-21; evidence below)
Date: 2026-10-20
Decisions: D2

## Context
A recorded clip of the user must not pass. A random phrase only defeats replay if the spoken content is checked. This is replay resistance against re-submitting a whole earlier recording. It is not liveness and not presentation attack detection (PAD).

## Decision
The server issues a single-use 4-word phrase from a CSPRNG, valid 60 s (configurable), bound to the user and action. A pass needs the ASR transcript to match the phrase and the speaker score to meet the threshold; both checks always run.

ASR is Whisper base.en (int8) through sherpa-onnx. The phrase matches when the letter-level similarity between the transcript and the phrase is 0.60 or higher (`1 - Levenshtein / longer length` over the letters of both texts, spaces and Whisper's `[...]` and `(...)` tags removed). The rule, the numeric quality gates and the spike are in `docs/design/verifier.md`. The challenge word list has 256 words, so a 4-word phrase carries 32 bits; the enrollment list shares no word with it.

## Spike evidence (T3.1)
Machine: WSL2, 20 logical CPUs, no GPU. 40 four-word phrases from the challenge list, spoken by 10 synthetic Piper LibriTTS-R voices (CC BY 4.0), resampled to 16 kHz.

| Whisper (int8) | p95 latency, ASR + embedding | Correct phrases accepted at 0.60 | Wrong phrase accepted | Exact word match, for comparison |
|---|---|---|---|---|
| tiny.en | 237 ms | 35 of 40 | 0 of 1,560 pairs | 13 of 40 |
| base.en (chosen) | 390 ms | 39 of 40 | 0 of 1,560 pairs (best wrong pair 0.33) | 13 of 40 |
| small.en | 885 ms | 38 of 40 | 0 of 1,560 pairs | 17 of 40 |

Gates, set before the base.en numbers were read: at least 90% of correct phrases accepted and no wrong phrase accepted; p95 latency inside the 3 s verifier timeout. Both are met. A second run of base.en on the same machine gave p50 319 ms and p95 411 ms; in a container (Ubuntu 24.04, same host) p50 322 ms and p95 424 ms.
Non-speech input (silence, noise, a tone) came back as bracketed tags or empty text, never words. A 0.3 s clip of "apple" came back as "app".

Limits of this evidence: the speech is synthetic, 40 phrases is small, and the voices read isolated nouns clearly. Accuracy on microphone speech is not measured; a miss sends the user to the fallback and is an ordinary failed voice attempt.

## Alternatives considered
- Fixed passphrase: trivially replayable.
- Speaker score only: no replay resistance at all.
- A dedicated anti-spoofing model: not selected; cost and licence unknown.
- Digits only: shorter to read, less ASR-robust.
- Cut the TTL to 15-20 s (review proposal): rejected. Reading and recording a 4-word phrase needs the time, and a shorter window does not stop splicing or voice conversion. The limit is stated instead.
- Exact word match: rejected by the spike, it accepted 13 of 40 correct phrases because Whisper splits and merges isolated nouns ("cliff peach" heard as "Lift each").
- Whisper tiny.en or small.en: tiny.en misses more correct phrases; small.en is 2 to 3 times slower for no gain here.

## Consequences
- Words spliced from recordings of the victim, and a cloned or converted voice that reads the phrase, can still pass; these are stated limits (threat model TM-001, TM-002). Enrollment phrases and past attempts are a word source for an attacker, so the challenge word list does not reuse enrollment words.
- The letter-similarity rule is looser than word match, and M6 measured how much (`docs/eval-report.md`, R4, LibriSpeech sentences of 3 to 6 words): a phrase with one wrong word is accepted 85.02% of the time on dev-clean and 87.00% on test-clean; correct phrases are accepted 96.04% (dev) and 99.55% (test); whole wrong phrases are accepted 0% of the time. The rule therefore stops replay of a different phrase, not a recording that differs by one word. Under this rule an attacker who holds recordings of 3 of the 4 challenge words passes the phrase check most of the time, so splicing needs 3 words, not 4, and the 32 bits of the phrase overstate the protection.
- Whisper can hallucinate on short or near-silent input; the quality gates reject such clips before ASR, and the bracketed tags are stripped before comparison.
- The threshold 0.60 came from 40 synthetic phrases; M6 re-checked it on LibriSpeech text (numbers above). It stays at 0.60 until the per-word rule of M6b Amendment 2 is evaluated; see the Review outcome section for what that run decided.
- A 4-word phrase is a probe of about 2 s. At that length the speaker check alone rejects 43.28% of genuine attempts (bootstrap 95% 37.84 to 48.51) at a threshold that accepts 2.04% of impostors. Voice is the occasional first try, and the fresh-MFA fallback is the normal path, not the exception.
