# Evaluation report (M6)

> **Speaker verification only.** These numbers describe the speaker-embedding score of the production code on LibriSpeech read speech. They are not the full false-reject rate of the product (ASR rejects are not included), they say nothing about spoofing, cloning, splicing or browser audio, and the test set is small. See [Not measured](#not-measured) and [Limits](#limits).

Protocol: `docs/design/evaluation-protocol.md`, committed as `f06f5e4` (amended in `de13bf3`) before any result existed. Reproduce with `scripts/eval.sh`.

## Headline (test-clean, primary condition P0)

At the default threshold 0.79 (chosen on dev-clean, frozen before test-clean was scored):

- False accept rate **2.04%** (478 of 23400 impostor trials); Wilson 95% [1.87%, 2.23%]; speaker-level bootstrap 95% [1.23%, 2.93%].
- False reject rate **43.28%** (2571 of 5940 genuine trials); Wilson 95% [42.03%, 44.55%]; speaker-level bootstrap 95% [37.84%, 48.51%].
- Equal error rate 10.69%; bootstrap 95% [9.06%, 12.37%].
- Best case (S0, full utterances): FAR 5.74%, FRR 3.84%, EER 4.59%.
- Speakers: 40. Distinct impostor speaker pairs: 1560.

What this means:

- With a 2 s probe, the speaker check alone sends about 4 in 10 genuine attempts to the fallback at this threshold. Voice would be the occasional first try, not the normal path.
- The same threshold gave FAR 0.68% on dev-clean and 2.04% on test-clean, above the test-clean bootstrap lower bound 1.23%: impostors in test-clean score higher than in dev-clean. A threshold chosen on 40 speakers does not carry over exactly to another 40.
- The threshold belongs to the P0 design (a 2 s probe). With full utterances (S0) the same threshold gives FAR 5.74% and FRR 3.84%: a different probe length needs its own operating point, and a longer phrase would help far more than any threshold.
- Noise hurts a lot: see P1 and P2. Moderate noise raises the EER well above the clean value, and the production gate refuses most audio below 10 dB SNR.

## Setup

| Item | Value |
|---|---|
| Speaker model id | `wespeaker-resnet34-lm:e9848563da86+sherpa-1.13.8+feat-v1` |
| Code path | `SherpaVerifier.AnalyzeAsync` (quality gates and embedding), `Cosine.NormalizedMean`, `Cosine.Similarity`, `PhraseGenerator.Similarity` |
| Seed | 6001 |
| dev-clean | 40 speakers, used only to choose the operating values |
| test-clean | 40 speakers, scored once at the frozen values |
| Per speaker | enrollment pool 8 utterances, probe pool up to 30 utterances (fewer for speakers with fewer long utterances, protocol amendment 1), 5 enrollments of 3 segments |
| Segment redraws after a quality-gate refusal | dev 18, test 6 |
| Bootstrap | speaker-level cluster bootstrap, 2000 resamples, percentile 95% |

Conditions:

- **P0**: clean, 3 x 2.5 s enrollment, 2 s probe (primary)
- **P1**: P0 probe + white noise, SNR 20 dB
- **P2**: P0 probe + white noise, SNR 10 dB
- **P3**: P0 probe + white noise, SNR 5 dB
- **P4**: P0 probe band-limited to 4 kHz (8 kHz sampling)
- **S0**: clean, 3 full utterances (<= 10 s), full-utterance probe (best case)

## Values frozen on dev-clean

| Value | Rule | Result | Applied in |
|---|---|---|---|
| Default threshold | R1: FAR 1% point in P0 on dev-clean, rounded up to 2 decimals | **0.79** | `StepUp:ThresholdSeed` |
| Strict threshold | R2: FAR 0.1% point, same rounding | 0.84 | reported only |
| Enrollment consistency floor | R3: 1st percentile of the lowest pairwise cosine of genuine enrollments | **0.46** | `StepUp:EnrollmentConsistencyFloor` |
| Phrase match threshold | R4 | **0.60** | `PhraseGenerator.MatchThreshold` |
| Cohort score normalisation | R5: adopt if P0 EER is 20% lower and no degraded EER rises | **not adopted** | scores stay raw cosines |

Rounding the threshold up to two decimals lowers FAR a little: on dev-clean the default threshold gives FAR 0.68% in P0 (the target was 1%).

Cohort normalisation, EER on dev-clean:

| Condition | Raw cosine | Cohort-normalised |
|---|---|---|
| P0 | 8.44% | 7.51% |
| P1 | 26.60% | 26.33% |
| P2 | 35.58% | 35.45% |
| P3 | n/a | n/a |
| P4 | 23.44% | 19.73% |

The criterion was not met, so nothing changes: scores are raw cosines.

## Results on test-clean

Threshold 0.79 (default) and 0.84 (strict). Each rate is followed by its Wilson 95% interval, which treats trials as independent and so **understates** the uncertainty, and by the speaker-level bootstrap 95% interval.

Threshold 0.79:

| Condition | FAR | FAR Wilson | FAR bootstrap | FRR | FRR Wilson | FRR bootstrap |
|---|---|---|---|---|---|---|
| P0 | 2.04% (478) | [1.87%, 2.23%] | [1.23%, 2.93%] | 43.28% (2571) | [42.03%, 44.55%] | [37.84%, 48.51%] |
| P1 | 2.02% (471) | [1.85%, 2.21%] | [1.09%, 3.08%] | 82.51% (4889) | [81.53%, 83.46%] | [77.24%, 87.66%] |
| P2 | 0.89% (184) | [0.77%, 1.03%] | [0.30%, 1.61%] | 96.11% (5041) | [95.55%, 96.60%] | [93.90%, 98.13%] |
| P3 | n/a (0) | [n/a, n/a] | [n/a, n/a] | n/a (0) | [n/a, n/a] | [n/a, n/a] |
| P4 | 0.05% (11) | [0.03%, 0.08%] | [0.00%, 0.11%] | 98.56% (5830) | [98.23%, 98.84%] | [96.90%, 99.97%] |
| S0 | 5.74% (1344) | [5.45%, 6.05%] | [3.97%, 7.68%] | 3.84% (228) | [3.38%, 4.36%] | [1.98%, 6.18%] |

Threshold 0.84:

| Condition | FAR | FAR Wilson | FAR bootstrap | FRR | FRR Wilson | FRR bootstrap |
|---|---|---|---|---|---|---|
| P0 | 0.32% (74) | [0.25%, 0.40%] | [0.15%, 0.52%] | 78.70% (4675) | [77.64%, 79.73%] | [73.61%, 83.32%] |
| P1 | 0.27% (62) | [0.21%, 0.34%] | [0.09%, 0.48%] | 96.15% (5697) | [95.63%, 96.61%] | [94.29%, 97.92%] |
| P2 | 0.06% (13) | [0.04%, 0.11%] | [0.00%, 0.14%] | 99.66% (5227) | [99.46%, 99.78%] | [99.33%, 99.92%] |
| P3 | n/a (0) | [n/a, n/a] | [n/a, n/a] | n/a (0) | [n/a, n/a] | [n/a, n/a] |
| P4 | 0.00% (1) | [0.00%, 0.02%] | [0.00%, 0.01%] | 99.90% (5909) | [99.78%, 99.95%] | [99.79%, 100.00%] |
| S0 | 1.35% (315) | [1.21%, 1.50%] | [0.67%, 2.12%] | 11.87% (705) | [11.07%, 12.72%] | [8.06%, 16.30%] |

| Condition | Genuine trials | Impostor trials | Speakers | Impostor speaker pairs | Probes not acquired | Enrollments not acquired | EER | EER bootstrap 95% |
|---|---|---|---|---|---|---|---|---|
| P0 | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 10.69% | [9.06%, 12.37%] |
| P1 | 5925 | 23339 | 40 | 1560 | 3 of 1188 | 0 of 200 | 27.35% | [24.69%, 29.78%] |
| P2 | 5245 | 20702 | 40 | 1560 | 139 of 1188 | 0 of 200 | 37.85% | [35.11%, 40.10%] |
| P3 | 0 | 0 | 40 | 0 | 1188 of 1188 | 0 of 200 | n/a | [n/a, n/a] |
| P4 | 5915 | 23289 | 40 | 1560 | 5 of 1188 | 0 of 200 | 26.14% | [23.30%, 29.05%] |
| S0 | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 4.59% | [3.17%, 6.21%] |

A probe or enrollment the production quality gates refuse is a failure to acquire: the product asks the user to try again, so it is excluded from FAR and FRR and counted here.

- **P1**: the production gate refused 3 of 1188 probes (0.25%); the rates above are for the probes that passed it.
- **P2**: the production gate refused 139 of 1188 probes (11.70%); the rates above are for the probes that passed it.
- **P3: the production gate refused every probe** (1188 of 1188), so this condition has no trials and no error rates. The gate refuses audio under 10 dB signal-to-noise (`Noisy`), which is the intended behaviour; it also means the speaker model's robustness at this noise level is not measured by this run.
- **P4**: the production gate refused 5 of 1188 probes (0.42%); the rates above are for the probes that passed it.

## Comparison with NIST SP 800-63B-4

NIST SP 800-63B-4 asks for a false match rate of 1 in 10,000 or better and a false non-match rate under 5%. It also says biometric comparison based on voice SHALL NOT be used. This project is a demonstrator and does not claim to meet any of it.

- Measured FAR at the default threshold in P0: 2.04% with 23400 impostor trials from 40 speakers; Wilson upper bound 2.23%, bootstrap upper bound 2.93%. The target is 0.01%.
- Zero errors in n independent trials bounds the rate at about 3/n with 95% confidence, so 1 in 10,000 needs about 30,000 independent impostor trials. This run has 23400 trials, but they are not independent: they are drawn from 40 speakers (1560 speaker pairs). **No number of trials drawn from this few speakers can demonstrate a false match rate of 1 in 10,000.**
- Measured FRR at the default threshold in P0: 43.28% (bootstrap upper bound 48.51%): not shown to be below the 5% target on this read-speech set. This ignores ASR rejects and real-world variability.
- At the strict threshold 0.84: FAR 0.32%, FRR 78.70%.

## Phrase check tolerance (R4)

Real read speech stands in for a spoken phrase: LibriSpeech utterances of 3 to 6 words and 1.5 to 4 s, the production Whisper transcript compared with the true text by `PhraseGenerator.Similarity`. A wrong phrase is another utterance's text; a near miss is the true text with one word replaced.

| Set | Candidates in range | Cases used | Refused by quality gate | Threshold | Correct accepted | Wrong accepted | Near miss accepted |
|---|---|---|---|---|---|---|---|
| dev-clean (chooses) | 237 | 234 | 7 | 0.60 | 96.04% | 0.00% | 85.02% |
| test-clean (reports) | 229 | 226 | 3 | 0.60 | 99.55% | 0.00% | 87.00% |

Rule: keep 0.60 if at least 90% of correct phrases and at most 1% of wrong phrases are accepted on dev-clean; otherwise the grid value described in the protocol.
The check separates different phrases (wrong phrases accepted: 0.00%) but not near-identical ones: a phrase with one wrong word is accepted 87.00% of the time on test-clean. It stops a replay of a whole different phrase, not a recording that differs by a word. The near-miss rate is shown for information and is not gated; the design response (longer phrases, a stricter word-level rule) is a follow-up, not part of M6.

## Enrollment consistency floor (R3)

| Set | Genuine enrollments | Below the floor | Poisoned enrollments (2 + 1 foreign segment) | Below the floor |
|---|---|---|---|---|
| dev-clean | 200 | 1.00% | 200 | 57.50% |
| test-clean | 200 | 1.50% | 200 | 47.50% |

Floor 0.46, from the first percentile of genuine enrollments on dev-clean. A poisoned enrollment under the floor is refused; the rest pass, so this check catches some, not all, foreign samples.

## dev-clean at the frozen values (for reference)

Threshold 0.79:

| Condition | FAR | FAR Wilson | FAR bootstrap | FRR | FRR Wilson | FRR bootstrap |
|---|---|---|---|---|---|---|
| P0 | 0.68% (158) | [0.58%, 0.79%] | [0.38%, 1.05%] | 43.24% (2566) | [41.98%, 44.50%] | [38.01%, 48.85%] |
| P1 | 1.15% (269) | [1.02%, 1.30%] | [0.40%, 2.12%] | 88.41% (5238) | [87.56%, 89.20%] | [82.45%, 93.48%] |
| P2 | 0.77% (158) | [0.66%, 0.90%] | [0.08%, 1.84%] | 97.34% (5047) | [96.86%, 97.74%] | [94.70%, 99.21%] |
| P3 | n/a (0) | [n/a, n/a] | [n/a, n/a] | n/a (0) | [n/a, n/a] | [n/a, n/a] |
| P4 | 0.00% (1) | [0.00%, 0.02%] | [0.00%, 0.01%] | 99.56% (5879) | [99.36%, 99.70%] | [98.83%, 100.00%] |
| S0 | 2.50% (586) | [2.31%, 2.71%] | [1.73%, 3.33%] | 4.82% (286) | [4.30%, 5.39%] | [2.21%, 8.24%] |

These rates were used to choose the values, so they are optimistic by construction. Only the test-clean table above is a result.

## Not measured

- The full false-reject rate: speaker rejects plus ASR phrase rejects together. Only the speaker score is evaluated here.
- Spoofing: replayed recordings, synthetic or cloned voices, and words spliced from a victim's recordings.
- Browser capture mismatch: echo cancellation, noise suppression, automatic gain, resampler quality, and a real microphone.
- Session variability: illness, mood, time of day and different devices between enrollment and probe.

## Limits

- Read English audiobook speech from 40 + 40 speakers: too few speakers for narrow intervals, and trials drawn from them are correlated.
- Genuine probes come from the same recording sessions as the enrollment audio (LibriSpeech chapters), so genuine scores are optimistic.
- Noise is synthetic white noise and the band limit is a filter; neither is real room or telephone audio.
- The 1% FAR operating point is a demonstrator choice, not a conformance claim. Scores are cosine similarities, not probabilities, and this report does not call them calibrated.
- The Wilson intervals assume independent trials and are lower bounds on the uncertainty. The bootstrap resamples claimed speakers; dependence through impostor-side speakers remains.

## Deviations from the protocol

- Percentiles use linear interpolation between ranks (the protocol did not name a method).
- Phrase cases the production quality gate refuses are excluded from the phrase rates and counted (the protocol did not say how to treat them).
- Dependence of the genuine/impostor trials of one probe on many claimed speakers is not modelled beyond the cluster bootstrap on the claimed speaker.
- Amendment 1 (probe pool sized to the utterances a speaker has) was made after a first full run stopped with an error on a speaker with too few long utterances. That run produced no result. A 3-speaker smoke run of the pipeline came earlier; no value was chosen from it or from any test-clean score.
- Reproducibility was checked by recomputing the embeddings of 2 speakers per set without the cache: they matched the cached ones exactly.

## Attribution

- LibriSpeech corpus (V. Panayotov, G. Chen, D. Povey, S. Khudanpur, ICASSP 2015), distributed by OpenSLR, resource 12, licence CC BY 4.0. Audio is fetched at run time and is not part of this repository.
- Speaker model: WeSpeaker ResNet34-LM (VoxCeleb), CC BY 4.0, run with sherpa-onnx (Apache-2.0). ASR: Whisper base.en (MIT). See `NOTICE`.

## Files

- `docs/eval/summary.csv`: every number in the tables above.
- `docs/eval/det-<set>-<condition>.csv`: 201 points of the DET curve per set and condition.
