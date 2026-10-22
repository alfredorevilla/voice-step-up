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

## 9. Amendments

### Amendment 1 (2026-10-21, before any score was computed)

Section 3 assumed every speaker has at least 38 utterances of 4.0 s or longer. Counting from the FLAC headers (no audio decoded, no score computed) shows 4 of 40 speakers in each set fall short; the smallest has 31 (dev-clean speakers 422, 2078, 2902, 3576; test-clean speakers 1221, 2094, 4077, 8224). The first full run stopped on speaker 422 with an error, before producing any result.

Rule, applied to every speaker alike: the enrollment pool stays at 8 utterances and the probe pool is `min(30, qualifying - 8)`, with a floor of 10 (a speaker below the floor would be excluded and listed; none is). Genuine trials per enrollment follow the speaker's own probe-pool size; impostor probes are 3 sampled from the other speaker's pool as before. Trial counts in section 3 are therefore upper bounds; the report states the actual counts.

### Amendment 2 (2026-10-21, before any new score is computed)

Why. The first report (commit `6d97692`) found that the 2 s probe of the 4-word challenge rejects 43.28% of genuine attempts at the default threshold, and that the letter-similarity phrase check accepts a phrase with one wrong word 85.02% (dev-clean) and 87.00% (test-clean) of the time. A panel (speaker-verification, architecture, security and product views) asked for a longer-probe re-evaluation and for a stricter phrase rule with a pre-registered gate. This amendment fixes both, and every rule below, before the code that produces a new score exists.

What was already seen. The test-clean numbers for conditions P0 to P4 and S0 were seen before this amendment. For that reason **every decision below is made on dev-clean only**, and test-clean is scored once for the new conditions at values frozen on dev-clean, as in sections 6 and 7. The report says that test-clean was used twice as a reporting set (once per protocol version) and that no choice depends on it.

**A2.1 New conditions.** Utterance pools, seeds, trials and the quality gate are unchanged, so P0 to P4 and S0 must reproduce the numbers of the first report exactly (the report states whether they do). Segments are cut from the same 4.0 s-or-longer utterances, so the longest segment is 4.0 s.

| Id | Enrollment | Probe | Role |
|---|---|---|---|
| L3 | 3 segments of 2.5 s, clean (as P0) | 3.0 s, clean | Isolates probe length |
| L3E | 3 segments of 3.5 s, clean | 3.0 s, clean | **Candidate** for a 6-word challenge |
| L4 | 3 segments of 2.5 s, clean | 4.0 s, clean | Isolates probe length, larger step |
| L4E | 3 segments of 3.5 s, clean | 4.0 s, clean | Explains the gain of a longer enrollment |

Words to seconds is an assumption, not a measurement: the product treats 4 words as about 2 s, 6 words as about 3 s and 8 words as about 4 s. The report gives the reading rate of the LibriSpeech phrase cases (words per second) as one hint and states that real challenge-word speech is not measured. Cohort normalisation (R5) stays restricted to P0 to P4.

**A2.2 Operating values per new condition.** Rule R1 applied to each of L3, L3E, L4, L4E on dev-clean: threshold at FAR at most 1%, rounded up to two decimals; rule R2 for the strict point. Rule R3 for the 3.5 s enrollment (L3E and L4E share the same enrollments): consistency floor from the first percentile of the lowest pairwise cosine, rounded down.

**A2.3 Adoption rule for a longer challenge (R6), dev-clean only.** The candidate is L3E, because a 6-word challenge is read for the probe and the enrollment phrases have the same length. Adopt `PhraseGenerator.WordsPerPhrase = 6` (enrollment phrases included), the L3E threshold as `StepUp:ThresholdSeed` and the L3E floor as `StepUp:EnrollmentConsistencyFloor` if and only if, on dev-clean at each condition's own R1 threshold, FRR(L3E) is at least 10 percentage points lower than FRR(P0) (FAR is at most 1% in both by construction). Otherwise keep 4 words, 0.79 and 0.46 and say so. Eight words (L4E) are not adopted by this amendment whatever the result: they change the demo flow more and the 8 s recorder cap leaves little margin. If L4E beats L3E by 10 points or more on dev-clean, the report states it as a candidate for a later decision.

**A2.4 Per-word phrase rule (R4'), dev-clean only.** The rule is added to the production code before it is evaluated (`PhraseGenerator`, no copy in the harness). Align the letters of the transcript with the letters of the expected phrase by Levenshtein edit distance (backtrace preference: match, substitute, delete a phrase letter, insert a transcript letter). The cost of each edit belongs to the expected word that holds the phrase letter, and an inserted transcript letter belongs to the word of the preceding phrase letter (the first word if none). Word similarity is `1 - cost / max(word letters, transcript letters aligned to the word)`. A phrase matches when the whole-phrase similarity is at least 0.60 (unchanged) **and** every expected word has similarity at least `w`. Order is enforced by the alignment; split and merged words are tolerated because the alignment runs on letters.

The cases are those of R4 (same seeded sample): correct, wrong whole phrase, and one-wrong-word. Grid for `w`: 0.50 to 0.70 in steps of 0.05. A value qualifies if on dev-clean **all** hold: correct phrases accepted at least 95%; one-wrong-word phrases accepted at most 5%; wrong whole phrases accepted exactly 0%. Choose the smallest qualifying `w`. If none qualifies, keep the whole-phrase rule at 0.60 alone and record that. The rule ships as `StepUp:PhraseMinWordSimilarity` (0 means off); the default is `w` if one qualifies, otherwise 0. The cases are LibriSpeech sentences of 3 to 6 words, not challenge-list nouns spoken by users, so the correct-phrase cost for real challenge speech is not measured; the report says so.

**A2.5 Reporting.** The report keeps its structure and adds the new conditions to every table, a table of the grid for `w` on dev-clean (all three rates per value, and the same rates on test-clean for the chosen `w`, scored once), the words-per-second hint, the decision text of R6 and R4', and a line saying whether P0 to P4 and S0 reproduced the first report. The existing limits stay; the limits of A2.1 and A2.4 are added.

**A2.6 Run.** Same command, same seeds, parallelism capped at 12 threads because the machine was shared (not a statistical choice). Per-case transcripts and expected texts of the phrase cases are written under the git-ignored output folder (text only, no audio) so the grid can be scored without audio.
