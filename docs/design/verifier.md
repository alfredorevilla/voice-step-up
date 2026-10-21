# Verifier design (M3)

Status: v1, written after the ASR spike (T3.1). Decisions: ADR-0001 (in-process verifier), ADR-0002 (phrase and ASR), ADR-0003 (own cosine, WAV capture). The numbers below are the contract for T3.2 to T3.5; tests pin them.

## 1. Models and pipeline

| Part | Choice | Notes |
|---|---|---|
| Runtime | `org.k2fsa.sherpa.onnx` 1.13.8 (Apache-2.0), CPU | Loads on Debian/glibc; not Alpine. |
| Speaker embedding | `wespeaker_en_voxceleb_resnet34_LM.onnx`, 256 dimensions | SHA-256 matches the release `checksum.txt`. |
| ASR | Whisper base.en, int8 encoder and decoder | Chosen in the spike (section 6). |
| Cosine | Computed by the Api from L2-normalised embeddings (ADR-0003) | The verifier never scores or decides. |

`IVerifier.AnalyzeAsync(wav, expectedPhrase, ct)` does: parse samples, quality gates, embedding, transcript. It returns a quality reject, or the probe embedding and transcript. The Api applies the phrase rule and the threshold.

`ModelId` = `wespeaker-resnet34-lm:<first 12 hex of the speaker model SHA-256>+sherpa-<package version>+feat-v1`. Only the speaker model and feature config matter: a stored embedding stays valid when the ASR model changes. Bump `feat-v1` when the sample handling changes.

## 2. Ingest limits

PCM16, mono, 16 kHz, at most 10 s and 1 MB, checked from the header before any sample is read (`WavInspector`, already in Core). A file that passes the header check but whose `data` chunk is shorter than declared is read up to the bytes present. Odd trailing bytes are ignored.

## 3. Quality gates

Samples are scaled to [-1, 1]. Frames are 20 ms (320 samples), non-overlapping; levels in dBFS (`10 log10(mean square)`).

Order of checks, first hit wins:

| # | Gate | Rule | Reason code |
|---|---|---|---|
| 1 | Muted | peak < 0.01 (-40 dBFS) or whole-clip RMS < 0.003 (-50 dBFS) | `Muted` |
| 2 | Clipped | at least 1% of samples have a magnitude of 32700 or more | `Noisy` (clipping maps to `Noisy`) |
| 3 | No speech | fewer than 0.8 s of net speech (below). If the median frame level is -40 dBFS or louder, the clip is loud non-speech | `Noisy` if loud non-speech, else `TooShort` |
| 4 | Low SNR | SNR below 10 dB | `Noisy` |

Net speech = frames at or above `max(noiseFloor + 10 dB, -45 dBFS)`, where `noiseFloor` is the 10th percentile of frame levels. SNR = `10 log10(mean power of speech frames / mean power of the quietest 10% of frames)`; the denominator is floored at 1e-10.

A quality reject does not consume the challenge, is logged as `Rejected`, and does not count toward the lockout (Api, M2). These energy gates are not a speech detector: music or a TV in the background can pass them. That is a stated limit.

## 4. Enrollment

The Api combines samples, not the verifier: each embedding is L2-normalised, the mean is taken, and the mean is normalised again. Samples are rejected if any pairwise cosine is below the consistency floor (0.5 placeholder, set from data in M6). Enrollment phrases come from the separate enrollment list and are ASR-checked with the same phrase rule as step-up.

## 5. Phrase check (ADR-0002)

Normalise both texts: lowercase, drop `[...]` and `(...)` groups (Whisper's non-speech tags), keep letters and digits, remove spaces. Similarity = `1 - Levenshtein(a, b) / max(len a, len b)` over those letters. **A phrase matches at similarity 0.60 or higher.** An empty transcript never matches.

Why letters and not words: Whisper often splits or merges isolated nouns ("cliff peach" heard as "Lift each"). A word-exact rule accepted only 13 of 40 correct phrases; the letter rule accepts 39 of 40 and rejected all 1,560 wrong-phrase pairs (best wrong-phrase similarity 0.33).

A failed phrase check is an ordinary failed voice attempt that offers the fallback.

Challenge word list: 256 words, 4 per phrase, so 32 bits of entropy (with repeats allowed). The enrollment list shares no word with it.

## 6. T3.1 spike result

Machine: WSL2, 20 logical CPUs, no GPU, 4 threads, int8 models. Audio: 40 four-word phrases from the challenge list, spoken by 10 Piper LibriTTS-R voices (CC BY 4.0), resampled to 16 kHz. This is synthetic speech, not microphone recordings; real-speech accuracy is unmeasured and is a limit.

| Whisper | p95 latency, ASR + embedding | Correct phrases accepted (similarity 0.60) | Wrong phrase accepted |
|---|---|---|---|
| tiny.en | 237 ms | 35 of 40 | 0 of 1,560 |
| base.en | 390 ms | 39 of 40 | 0 of 1,560 |
| small.en | 885 ms | 38 of 40 (one clip at 0.15) | 0 of 1,560 |

Go with base.en. Accuracy gate (set before reading the result for base.en): at least 90% correct accepted and no wrong-phrase accepted in the spike set. Latency gate: p95 inside the 3 s verifier timeout; measured 390 ms. Non-speech (silence, noise, a tone) came back as bracketed tags or empty text, never as words; a 0.3 s clip of "apple" came back as "app". The gates in section 3 reject such clips before ASR runs.

Speaker separation on TTS voices (three voices, nine utterances each): same-voice cosine mean 0.76, different-voice mean 0.50, with some different pairs up to 0.78. TTS voices are a test stand-in only; speaker accuracy is measured in M6.

## 7. Concurrency and timeout

One `SpeakerEmbeddingExtractor` and one `OfflineRecognizer` for the process, created at startup. Each request makes its own stream. A semaphore bounds concurrent analyses (default 2); a request that cannot enter within the verifier timeout gets `RateLimited` from the Api's gate (M2). Threads per model: `Verifier:Threads`, default 2. The native calls cannot be cancelled mid-flight: the verifier checks the cancellation token before and between steps, and the Api's fail-closed timeout (3 s) covers the rest.

## 8. Startup checks

At startup the verifier reads `models/models.lock`, hashes each model file it uses and refuses to start on a missing file or a hash mismatch. Models are fetched by `scripts/fetch-models.sh`, never committed.
