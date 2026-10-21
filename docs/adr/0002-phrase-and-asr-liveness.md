# ADR-0002: Replay resistance by random phrase plus ASR check

Status: Accepted (ASR spike T3.1 done 2026-10-21; evidence below)
Date: 2026-10-20
Decisions: D2

## Context
A recorded clip of the user must not pass. A random phrase only defeats replay if the spoken content is checked. This is replay resistance against re-submitting a whole earlier recording. It is not liveness and not presentation attack detection (PAD).

## Decision
The server issues a single-use 6-word phrase from a CSPRNG, valid 60 s (configurable), bound to the user and action. A pass needs the ASR transcript to match the phrase and the speaker score to meet the threshold; both checks always run.

ASR is Whisper base.en (int8) through sherpa-onnx. The phrase matches when the letter-level similarity between the transcript and the phrase is 0.60 or higher (`1 - Levenshtein / longer length` over the letters of both texts, spaces and Whisper's `[...]` and `(...)` tags removed). The rule, the numeric quality gates and the spike are in `docs/design/verifier.md`. The challenge word list has 256 words, so a 6-word phrase carries 48 bits; the enrollment list (80 words, also 6 per phrase) shares no word with it. The phrase had 4 words (32 bits) until M6b Amendment 2 moved it to 6 (rule R6, see Amendment 2 outcome below).

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
- Cut the TTL to 15-20 s (review proposal): rejected. Reading and recording a 6-word phrase needs the time, and a shorter window does not stop splicing or voice conversion. The limit is stated instead.
- Exact word match: rejected by the spike, it accepted 13 of 40 correct phrases because Whisper splits and merges isolated nouns ("cliff peach" heard as "Lift each").
- Whisper tiny.en or small.en: tiny.en misses more correct phrases; small.en is 2 to 3 times slower for no gain here.

## Consequences
- Words spliced from recordings of the victim, and a cloned or converted voice that reads the phrase, can still pass; these are stated limits (threat model TM-001, TM-002). Enrollment phrases and past attempts are a word source for an attacker, so the challenge word list does not reuse enrollment words.
- The letter-similarity rule is looser than word match, and M6 measured how much (`docs/eval-report.md`, R4, LibriSpeech sentences of 3 to 6 words): a phrase with one wrong word is accepted 85.02% of the time on dev-clean and 87.00% on test-clean; correct phrases are accepted 96.04% (dev) and 99.55% (test); whole wrong phrases are accepted 0% of the time. The rule therefore stops replay of a different phrase, not a recording that differs by one word. Those rates were measured on sentences of 3 to 6 words, not on challenge phrases, and not for 6 challenge words. Under this rule an attacker who holds recordings of all but one challenge word is expected to pass the phrase check most of the time, so splicing needs fewer words than the phrase has, and the 48 bits of the 6-word phrase overstate the protection.
- Whisper can hallucinate on short or near-silent input; the quality gates reject such clips before ASR, and the bracketed tags are stripped before comparison.
- The threshold 0.60 came from 40 synthetic phrases; M6 re-checked it on LibriSpeech text (numbers above). It stays at 0.60: the per-word rule of M6b Amendment 2 was evaluated and no floor met its gate (see Amendment 2 outcome below).
- A 4-word phrase is a probe of about 2 s. At that length the speaker check alone rejected 43.28% of genuine attempts on test-clean (bootstrap 95% 37.84 to 48.51) at a threshold that accepted 2.04% of impostors (first report, condition P0). A 6-word phrase is about 3 s (condition L3E): on test-clean FRR 23.82% (bootstrap 95% 19.53 to 28.34) and FAR 2.18% at threshold 0.80. Voice is still the first try for most users and the fresh-MFA fallback is offered after every failure; roughly 1 genuine attempt in 4 still falls back.

## Amendment 2 outcome (2026-10-21)
Protocol: `docs/design/evaluation-protocol.md`, Amendment 2, committed before any new score; results: `docs/eval-report.md`.
- Rule R6 (dev-clean) was met: FRR(P0) 43.24% against FRR(L3E) 23.44%, a gain of 19.8 percentage points against the required 10. The challenge and the enrollment phrases move from 4 to 6 words (`PhraseGenerator.WordsPerPhrase = 6`), the speaker threshold seed to 0.80 and the enrollment consistency floor to 0.43. These values belong to a probe of about 3 s and to the speaker model id. Eight words (L4E) were not adopted: FRR(L4E) 18.06% on dev-clean is not 10 points better than L3E.
- The per-word phrase rule (every expected word must reach a letter-similarity floor `w`) is implemented behind `StepUp:PhraseMinWordSimilarity` and stays off. No `w` from 0.50 to 0.70 met the gate on dev-clean (correct phrases accepted at least 95%, one-wrong-word phrases at most 5%, wrong whole phrases exactly 0%): at 0.50 correct phrases pass 86.78%, and from 0.55 up one-wrong-word phrases pass 0.00% but correct phrases drop to between 82.38% and 74.89%.
- The seconds per word are an assumption (the LibriSpeech phrase cases are read at 1.89 words per second on dev-clean); challenge words spoken by users are not measured.
- The phrase check is unchanged: the whole-phrase letter rule at 0.60.
