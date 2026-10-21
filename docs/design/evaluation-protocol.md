# Evaluation protocol (M6, pre-registered)

Status: written and committed before the harness exists and before any score has been computed. The report (`docs/eval-report.md`) cites the commit of this file. Anything the report does differently from this file is listed in its "Deviations" section.

This protocol measures **speaker verification only**: the cosine score of the production embedding path. It does not measure the full false-reject rate (ASR rejects plus speaker rejects), spoofing, splicing, or browser-capture mismatch. Those are listed as not measured in the report.

## 1. Data

| Set | Source | Licence | Use |
|---|---|---|---|
| `dev-clean` | LibriSpeech, OpenSLR resource 12, 40 speakers | CC BY 4.0 (OpenSLR page) | Choose every operating value |
| `test-clean` | LibriSpeech, OpenSLR resource 12, 40 speakers | CC BY 4.0 | Report only, at the values frozen on `dev-clean` |

- Archives are fetched at run time into a git-ignored folder. The MD5 of each archive is checked against the value in `eval/librispeech.md5`, copied from OpenSLR's `md5sum.txt` (retrieved 2026-10-21). No audio is committed.
- FLAC is decoded to 16 kHz mono PCM16 with `ffmpeg`. The eval tool is the only code that needs `ffmpeg`.
- LibriSpeech is read English audiobook speech, from MP3 sources. The speaker model was trained on VoxCeleb, so neither set was seen in training.
- No result from `test-clean` may influence any choice. `test-clean` is scored once per run of the frozen configuration.

## 2. Code under test

The harness calls the production code and does not copy it:

- Embedding and transcript: `SherpaVerifier.AnalyzeAsync` on WAV bytes. Its quality gates apply.
- Enrollment template: `Cosine.NormalizedMean` over the 3 sample embeddings.
- Score: `Cosine.Similarity(template, probe)`.
- Phrase check: `PhraseGenerator.Similarity` and `PhraseGenerator.Matches`.

An equivalence test (`Category=Model`) scores one trial through the harness and through the same calls made by hand, and requires identical results.

## 3. Trial design

Master seed: `6001`. Every random choice derives from it, so a run is reproducible.

Per speaker, the utterances of at least 4.0 s are shuffled with the seed. The first 8 form the **enrollment pool**; the next 30 form the **probe pool**. The two pools never share an utterance.

**Enrollments.** Each speaker gets 5 repetitions. A repetition draws 3 distinct utterances from the enrollment pool and takes one segment from each. A segment that fails the production quality gate is redrawn (different offset), at most 10 times; the number of redraws is reported.

**Probes.** Each probe-pool utterance gives one probe segment. Degradations are applied to the same segment.

**Trials.** For every enrollment: all 30 probes of the same speaker (genuine), and 3 probes sampled from each of the other 39 speakers' pools (impostor). That is 6,000 genuine and 23,400 impostor trials per condition per set, before acquisition failures.

**Acquisition failures.** A probe or enrollment rejected by the production quality gates in a condition is a failure to acquire (the product asks for a retry). It is excluded from FAR and FRR and reported as a count and rate per condition.

## 4. Conditions

| Id | Enrollment | Probe | Role |
|---|---|---|---|
| P0 | 3 segments of 2.5 s, clean | 2.0 s, clean | **Primary** (matches the product) |
| P1 | as P0 | P0 with white noise at SNR 20 dB | Degraded |
| P2 | as P0 | P0 with white noise at SNR 10 dB | Degraded |
| P3 | as P0 | P0 with white noise at SNR 5 dB | Degraded |
| P4 | as P0 | P0 band-limited to 8 kHz sampling (4 kHz bandwidth), then back to 16 kHz | Degraded |
| S0 | 3 full utterances (first 10 s), clean | full utterance (first 10 s), clean | Secondary, best case |

Noise is white Gaussian noise scaled to the stated SNR over the probe's speech-active energy (the mean power of the whole segment). Enrollment is always clean: enrollment happens once, in the user's chosen conditions, which this protocol does not model.

## 5. Metrics

At threshold `t`: FAR = share of impostor trials with score `>= t`; FRR = share of genuine trials with score `< t`. EER is reported per condition for information only.

Intervals (95%), always printed with the number of speakers and of distinct claimed-by-probe speaker pairs:

- Wilson interval on the trial proportion. Trials are not independent, so this is a **lower bound on the uncertainty**.
- Speaker-level cluster bootstrap: resample the claimed speakers with replacement (B = 2000, seed 6001), carrying each speaker's genuine and impostor trials with them; percentile interval. Dependence through impostor-side speakers remains; the report says so.

NIST SP 800-63B-4 comparison: false match rate of 1 in 10,000 or better, false non-match rate under 5%. The report states the measured FAR with its upper bound, the number of trials, and that any number of trials drawn from 40 speakers cannot demonstrate a rate of 1 in 10,000. It does not claim the project meets the target. It does not use the word "calibrated" for scores.

## 6. Decision rules (all applied on `dev-clean`, then frozen)

**R1. Default threshold.** `t*` = the smallest threshold at which FAR is at most 1% in condition P0 on `dev-clean`, rounded up to two decimals. This FAR is a demonstrator choice, not a conformance claim. `t*` becomes `StepUp:ThresholdSeed`. If `t*` is below `ThresholdFloor` (0.30), the floor is raised to `t*`.

**R2. Strict point.** `t_strict` = the same rule at FAR 0.1%. Reported, not configured.

**R3. Enrollment consistency floor.** For each enrollment on `dev-clean` (P0 design), take the lowest pairwise cosine among its 3 sample embeddings. The floor `f` is the 1st percentile of those values, rounded down to two decimals. It becomes `StepUp:EnrollmentConsistencyFloor`. On `test-clean` the report gives the rate at which genuine enrollments fall below `f`, and the rate at which a poisoned enrollment (2 segments of speaker A and 1 of a random other speaker B) falls below `f`.

**R4. Phrase-match tolerance.** Take dev-clean utterances of 3 to 6 words and 1.5 to 4.0 s (seeded sample, up to 300). A correct case compares the transcript with the utterance's own text; a wrong case compares it with the text of another sampled utterance. Keep `PhraseGenerator.MatchThreshold` (0.60) if on `dev-clean` at least 90% of correct cases are accepted and at most 1% of wrong cases are accepted. Otherwise choose the threshold in steps of 0.05 that keeps wrong-case acceptance at or below 1% and, among those, maximises correct-case acceptance (ties go to the higher threshold). The report gives both rates on `test-clean` at the frozen value. A near-miss set (one word of the true text replaced by a word from another utterance) is reported for information; it is not part of the rule.

**R5. Cohort score normalisation.** Experiment: for each trial, normalise the score as `(score - mean) / std`, where mean and std are taken from the cosines between the probe and all enrollment templates of speakers other than the claimed speaker and the probe's speaker (leave-two-speakers-out cohort, same set). Adopt only if on `dev-clean` it lowers the P0 EER by at least 20% (relative) and does not raise the EER in any of P1 to P4. If the criterion is met, shipping it is a separate change (a stored cohort set with its own id in `ModelId` and `Attempts`, and a licence note) and is out of scope for M6; the report then recommends it and the scores stay raw cosines. If it is not met, nothing changes.

## 7. Reporting

`docs/eval-report.md` contains: the commit of this protocol; the frozen values (`t*`, `t_strict`, `f`, the phrase threshold, the cohort decision) with the `dev-clean` evidence that produced them; for `test-clean` and each condition, FAR and FRR at `t*` and at `t_strict`, trial counts, acquisition failures, both intervals and EER; the NIST comparison; the dataset attribution; a "Deviations" section; and a limits section.

Limits stated in advance:

- Read speech, 40 + 40 speakers: too few to demonstrate a false match rate of 1 in 10,000 and too few for narrow intervals.
- Genuine probes come from the same recording sessions as the enrollment audio (LibriSpeech chapters), so genuine scores are optimistic: no illness, mood, microphone or time-of-day change.
- White noise and a band limit are not real room noise or browser audio processing; browser capture mismatch is not measured.
- No presentation-attack data: spoofing, cloning, splicing and replay are not measured.
- ASR and speaker rejects are not combined: the full false-reject rate is not measured.
- The 1% FAR operating point is a demonstrator choice. The model is not claimed to be "calibrated": scores are cosines, not probabilities.

## 8. Reproduction

`scripts/eval.sh` fetches and checks the data, builds the tool, runs the protocol and writes `docs/eval-report.md` plus the CSV files under `docs/eval/`. Required tools: `dotnet`, `ffmpeg`, `curl`, `md5sum`, and the models from `scripts/fetch-models.sh`.
