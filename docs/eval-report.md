# Evaluation report (M6)

> **Speaker verification only.** These numbers describe the speaker-embedding score of the production code on LibriSpeech read speech. They are not the full false-reject rate of the product (ASR rejects are not included), they say nothing about spoofing, cloning, splicing or browser audio, and the test set is small. See [Not measured](#not-measured) and [Limits](#limits).

Protocol: `docs/design/evaluation-protocol.md`, committed as `f06f5e4` (amended in `632cb23`) before any result existed. Reproduce with `scripts/eval.sh`.

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
- **L3**: clean, 3 x 2.5 s enrollment, 3 s probe
- **L3E**: clean, 3 x 3.5 s enrollment, 3 s probe (candidate for a 6-word challenge)
- **L4**: clean, 3 x 2.5 s enrollment, 4 s probe
- **L4E**: clean, 3 x 3.5 s enrollment, 4 s probe

## Values frozen on dev-clean

| Value | Rule | Result | Applied in |
|---|---|---|---|
| Default threshold | R1: FAR 1% point in P0 on dev-clean, rounded up to 2 decimals | **0.79** | `StepUp:ThresholdSeed` |
| Strict threshold | R2: FAR 0.1% point, same rounding | 0.84 | reported only |
| Enrollment consistency floor | R3: 1st percentile of the lowest pairwise cosine of genuine enrollments | **0.46** | `StepUp:EnrollmentConsistencyFloor` |
| Phrase match threshold | R4 | **0.60** | `PhraseGenerator.MatchThreshold` |
| Cohort score normalisation | R5: adopt if P0 EER is 20% lower and no degraded EER rises | **not adopted** | scores stay raw cosines |
| Threshold for L3 | A2.2: R1 in L3 on dev-clean | 0.79 (strict 0.86) | reported only |
| Threshold for L3E | A2.2: R1 in L3E on dev-clean | 0.80 (strict 0.85) | `StepUp:ThresholdSeed` |
| Threshold for L4 | A2.2: R1 in L4 on dev-clean | 0.80 (strict 0.86) | reported only |
| Threshold for L4E | A2.2: R1 in L4E on dev-clean | 0.81 (strict 0.86) | reported only |
| Consistency floor for 3.5 s enrollment | A2.2: R3 on the L3E/L4E enrollments | 0.43 | `StepUp:EnrollmentConsistencyFloor` |
| 6-word challenge | A2.3: FRR(L3E) at least 10 points below FRR(P0) on dev-clean | **adopted** | `PhraseGenerator.WordsPerPhrase` |
| Per-word phrase floor | A2.4: smallest w in 0.50 to 0.70 meeting the gate on dev-clean | **none qualifies** | `StepUp:PhraseMinWordSimilarity` |

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

Threshold 0.79 for P0 to P4 and S0; each of L3, L3E, L4, L4E uses its own (column Threshold):

| Condition | Threshold | FAR | FAR Wilson | FAR bootstrap | FRR | FRR Wilson | FRR bootstrap |
|---|---|---|---|---|---|---|---|
| P0 | 0.79 | 2.04% (478) | [1.87%, 2.23%] | [1.23%, 2.93%] | 43.28% (2571) | [42.03%, 44.55%] | [37.84%, 48.51%] |
| P1 | 0.79 | 2.02% (471) | [1.85%, 2.21%] | [1.09%, 3.08%] | 82.51% (4889) | [81.53%, 83.46%] | [77.24%, 87.66%] |
| P2 | 0.79 | 0.89% (184) | [0.77%, 1.03%] | [0.30%, 1.61%] | 96.11% (5041) | [95.55%, 96.60%] | [93.90%, 98.13%] |
| P3 | 0.79 | n/a (0) | [n/a, n/a] | [n/a, n/a] | n/a (0) | [n/a, n/a] | [n/a, n/a] |
| P4 | 0.79 | 0.05% (11) | [0.03%, 0.08%] | [0.00%, 0.11%] | 98.56% (5830) | [98.23%, 98.84%] | [96.90%, 99.97%] |
| S0 | 0.79 | 5.74% (1344) | [5.45%, 6.05%] | [3.97%, 7.68%] | 3.84% (228) | [3.38%, 4.36%] | [1.98%, 6.18%] |
| L3 | 0.79 | 2.82% (659) | [2.61%, 3.04%] | [1.79%, 3.95%] | 25.25% (1500) | [24.16%, 26.37%] | [20.77%, 30.03%] |
| L3E | 0.80 | 2.18% (509) | [2.00%, 2.37%] | [1.31%, 3.14%] | 23.82% (1415) | [22.76%, 24.92%] | [19.53%, 28.34%] |
| L4 | 0.80 | 2.94% (688) | [2.73%, 3.16%] | [1.87%, 4.10%] | 21.01% (1248) | [19.99%, 22.06%] | [16.79%, 25.67%] |
| L4E | 0.81 | 2.21% (518) | [2.03%, 2.41%] | [1.32%, 3.21%] | 19.24% (1143) | [18.26%, 20.26%] | [15.17%, 23.65%] |

Threshold 0.84 for P0 to P4 and S0; each of L3, L3E, L4, L4E uses its own (column Threshold):

| Condition | Threshold | FAR | FAR Wilson | FAR bootstrap | FRR | FRR Wilson | FRR bootstrap |
|---|---|---|---|---|---|---|---|
| P0 | 0.84 | 0.32% (74) | [0.25%, 0.40%] | [0.15%, 0.52%] | 78.70% (4675) | [77.64%, 79.73%] | [73.61%, 83.32%] |
| P1 | 0.84 | 0.27% (62) | [0.21%, 0.34%] | [0.09%, 0.48%] | 96.15% (5697) | [95.63%, 96.61%] | [94.29%, 97.92%] |
| P2 | 0.84 | 0.06% (13) | [0.04%, 0.11%] | [0.00%, 0.14%] | 99.66% (5227) | [99.46%, 99.78%] | [99.33%, 99.92%] |
| P3 | 0.84 | n/a (0) | [n/a, n/a] | [n/a, n/a] | n/a (0) | [n/a, n/a] | [n/a, n/a] |
| P4 | 0.84 | 0.00% (1) | [0.00%, 0.02%] | [0.00%, 0.01%] | 99.90% (5909) | [99.78%, 99.95%] | [99.79%, 100.00%] |
| S0 | 0.84 | 1.35% (315) | [1.21%, 1.50%] | [0.67%, 2.12%] | 11.87% (705) | [11.07%, 12.72%] | [8.06%, 16.30%] |
| L3 | 0.86 | 0.21% (48) | [0.15%, 0.27%] | [0.06%, 0.39%] | 74.53% (4427) | [73.40%, 75.62%] | [68.58%, 79.70%] |
| L3E | 0.85 | 0.30% (71) | [0.24%, 0.38%] | [0.12%, 0.53%] | 58.42% (3470) | [57.16%, 59.67%] | [52.13%, 64.27%] |
| L4 | 0.86 | 0.28% (66) | [0.22%, 0.36%] | [0.11%, 0.51%] | 63.75% (3787) | [62.52%, 64.97%] | [57.66%, 69.63%] |
| L4E | 0.86 | 0.30% (70) | [0.24%, 0.38%] | [0.11%, 0.54%] | 52.69% (3130) | [51.42%, 53.96%] | [46.15%, 58.75%] |

| Condition | Genuine trials | Impostor trials | Speakers | Impostor speaker pairs | Probes not acquired | Enrollments not acquired | EER | EER bootstrap 95% |
|---|---|---|---|---|---|---|---|---|
| P0 | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 10.69% | [9.06%, 12.37%] |
| P1 | 5925 | 23339 | 40 | 1560 | 3 of 1188 | 0 of 200 | 27.35% | [24.69%, 29.78%] |
| P2 | 5245 | 20702 | 40 | 1560 | 139 of 1188 | 0 of 200 | 37.85% | [35.11%, 40.10%] |
| P3 | 0 | 0 | 40 | 0 | 1188 of 1188 | 0 of 200 | n/a | [n/a, n/a] |
| P4 | 5915 | 23289 | 40 | 1560 | 5 of 1188 | 0 of 200 | 26.14% | [23.30%, 29.05%] |
| S0 | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 4.59% | [3.17%, 6.21%] |
| L3 | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 8.38% | [6.92%, 10.00%] |
| L3E | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 7.67% | [6.25%, 9.16%] |
| L4 | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 7.61% | [6.05%, 9.18%] |
| L4E | 5940 | 23400 | 40 | 1560 | 0 of 1188 | 0 of 200 | 6.73% | [5.22%, 8.27%] |

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

## Per-word phrase rule (Amendment 2, A2.4)

The whole-phrase similarity must reach 0.60 (unchanged) and, with the rule on, every expected word must reach the floor `w` (`PhraseGenerator.WordSimilarities`: letters aligned by Levenshtein distance, an edit belongs to the expected word holding the letter). Same cases as R4. Gate on dev-clean: correct phrases accepted at least 95%, one-wrong-word phrases at most 5%, wrong whole phrases exactly 0%; the smallest qualifying `w` is chosen.

| Rule | dev-clean correct | dev-clean one wrong word | dev-clean wrong phrase | Gate |
|---|---|---|---|---|
| whole phrase only (off) | 96.04% | 85.02% | 0.00% | not applicable |
| every word >= 0.50 | 86.78% | 2.64% | 0.00% | not met |
| every word >= 0.55 | 82.38% | 0.00% | 0.00% | not met |
| every word >= 0.60 | 81.06% | 0.00% | 0.00% | not met |
| every word >= 0.65 | 78.85% | 0.00% | 0.00% | not met |
| every word >= 0.70 | 74.89% | 0.00% | 0.00% | not met |

**No floor met the gate on dev-clean**, so the per-word rule is not turned on: the whole-phrase rule at 0.60 stays alone, and a phrase with one wrong word is still accepted at the rate shown in the first row. `StepUp:PhraseMinWordSimilarity` stays 0 (off).

Limits: the cases are LibriSpeech sentences of 3 to 6 words, not challenge-list words spoken by users, so the cost of the rule for real challenge speech (Whisper splitting and merging isolated nouns) is not measured here; the earlier synthetic-voice spike (ADR-0002) used exact whole-phrase matching only. A miss sends the user to the fallback. The rule narrows splicing (an attacker must now match every word), it does not close it.

## Longer challenge (Amendment 2, A2.1 to A2.3)

A 4-word challenge is a probe of about 2 s (P0). Amendment 2 adds probes of 3 s and 4 s, each with the 2.5 s enrollment of P0 or a 3.5 s enrollment, and gives every condition its own R1 threshold on dev-clean. Words to seconds is an assumption: the phrase cases of this run are read at 1.89 words per second (dev-clean) and 1.95 (test-clean) in running audiobook speech, so 6 words would be about 3.2 s and 4 words about 2.1 s at that rate; spoken challenge words (isolated nouns) are not measured.

dev-clean (chooses), each condition at its own R1 threshold:

| Condition | Threshold | FAR | FRR | FRR bootstrap 95% | EER | Probes not acquired |
|---|---|---|---|---|---|---|
| P0 | 0.79 | 0.68% | 43.24% | [38.01%, 48.85%] | 8.44% | 0 of 1187 |
| L3 | 0.79 | 0.98% | 24.77% | [19.45%, 30.63%] | 6.47% | 0 of 1187 |
| L3E | 0.80 | 0.73% | 23.44% | [18.43%, 29.00%] | 5.94% | 0 of 1187 |
| L4 | 0.80 | 0.86% | 19.80% | [15.05%, 25.25%] | 5.39% | 0 of 1187 |
| L4E | 0.81 | 0.69% | 18.06% | [13.53%, 23.33%] | 4.97% | 0 of 1187 |

test-clean (reports, scored once), each condition at its own R1 threshold:

| Condition | Threshold | FAR | FRR | FRR bootstrap 95% | EER | Probes not acquired |
|---|---|---|---|---|---|---|
| P0 | 0.79 | 2.04% | 43.28% | [37.84%, 48.51%] | 10.69% | 0 of 1188 |
| L3 | 0.79 | 2.82% | 25.25% | [20.77%, 30.03%] | 8.38% | 0 of 1188 |
| L3E | 0.80 | 2.18% | 23.82% | [19.53%, 28.34%] | 7.67% | 0 of 1188 |
| L4 | 0.80 | 2.94% | 21.01% | [16.79%, 25.67%] | 7.61% | 0 of 1188 |
| L4E | 0.81 | 2.21% | 19.24% | [15.17%, 23.65%] | 6.73% | 0 of 1188 |

Rule R6 (dev-clean): FRR(P0) 43.24%, FRR(L3E) 23.44%, a gain of 19.8 percentage points against the required 10.

**R6 is met.** The product moves to 6-word challenges and enrollment phrases: `PhraseGenerator.WordsPerPhrase = 6`, `StepUp:ThresholdSeed` is the L3E threshold and `StepUp:EnrollmentConsistencyFloor` is the 3.5 s floor above. These values belong to a probe of about 3 s and to the speaker model id; they do not carry over to another length.

Eight words (L4E, about 4 s) are not adopted by this amendment whatever the result. On dev-clean FRR(L4E) is 18.06%, not 10 points better than L3E.

Comparing L3 with L3E separates probe length from enrollment length; the data cannot say more than that. Genuine probes still come from the same sessions as the enrollment audio, so every FRR here is optimistic.

## Enrollment consistency floor (R3)

| Set | Genuine enrollments | Below the floor | Poisoned enrollments (2 + 1 foreign segment) | Below the floor |
|---|---|---|---|---|
| dev-clean | 200 | 1.00% | 200 | 57.50% |
| test-clean | 200 | 1.50% | 200 | 47.50% |

Floor 0.46, from the first percentile of genuine enrollments on dev-clean. A poisoned enrollment under the floor is refused; the rest pass, so this check catches some, not all, foreign samples.

## dev-clean at the frozen values (for reference)

Threshold 0.79 for P0 to P4 and S0; each of L3, L3E, L4, L4E uses its own (column Threshold):

| Condition | Threshold | FAR | FAR Wilson | FAR bootstrap | FRR | FRR Wilson | FRR bootstrap |
|---|---|---|---|---|---|---|---|
| P0 | 0.79 | 0.68% (158) | [0.58%, 0.79%] | [0.38%, 1.05%] | 43.24% (2566) | [41.98%, 44.50%] | [38.01%, 48.85%] |
| P1 | 0.79 | 1.15% (269) | [1.02%, 1.30%] | [0.40%, 2.12%] | 88.41% (5238) | [87.56%, 89.20%] | [82.45%, 93.48%] |
| P2 | 0.79 | 0.77% (158) | [0.66%, 0.90%] | [0.08%, 1.84%] | 97.34% (5047) | [96.86%, 97.74%] | [94.70%, 99.21%] |
| P3 | 0.79 | n/a (0) | [n/a, n/a] | [n/a, n/a] | n/a (0) | [n/a, n/a] | [n/a, n/a] |
| P4 | 0.79 | 0.00% (1) | [0.00%, 0.02%] | [0.00%, 0.01%] | 99.56% (5879) | [99.36%, 99.70%] | [98.83%, 100.00%] |
| S0 | 0.79 | 2.50% (586) | [2.31%, 2.71%] | [1.73%, 3.33%] | 4.82% (286) | [4.30%, 5.39%] | [2.21%, 8.24%] |
| L3 | 0.79 | 0.98% (229) | [0.86%, 1.11%] | [0.65%, 1.34%] | 24.77% (1470) | [23.69%, 25.88%] | [19.45%, 30.63%] |
| L3E | 0.80 | 0.73% (170) | [0.63%, 0.84%] | [0.41%, 1.10%] | 23.44% (1391) | [22.38%, 24.53%] | [18.43%, 29.00%] |
| L4 | 0.80 | 0.86% (201) | [0.75%, 0.99%] | [0.53%, 1.22%] | 19.80% (1175) | [18.80%, 20.83%] | [15.05%, 25.25%] |
| L4E | 0.81 | 0.69% (162) | [0.59%, 0.81%] | [0.38%, 1.06%] | 18.06% (1072) | [17.10%, 19.06%] | [13.53%, 23.33%] |

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
- Amendment 2 (docs/design/evaluation-protocol.md, committed before any new score) added the conditions L3, L3E, L4, L4E and the per-word phrase gate. The test-clean numbers for P0 to P4 and S0 had been seen under the first protocol version; every Amendment 2 decision uses dev-clean only, and test-clean is scored once per condition at values frozen on dev-clean.
- P0 to P4 and S0 reproduce the first report exactly: every P0 to P4 and S0 row of `docs/eval/summary.csv` is byte-identical to the first report's (the only changes to the file are the added L3, L3E, L4 and L4E rows). The run's own check printed 12 of 144 differences, all P3 on both sets (FAR, FRR and EER at both thresholds, "n/a" before, "NaN" now): P3 has no trials, and the check spelled a missing score differently from the CSV writer. `Reproduction.Compare` now spells it "n/a" and has a test; the evaluation was not run again.
- P3 produced no scores on dev-clean (the production gate refused every probe), so rule R5's "EER not higher in P1 to P4" had nothing to compare for it. The harness reads such a condition as not worse. The protocol did not say this; if it had a score the 20% bar for cohort normalisation could have failed on it. The condition is also unmeasured in the results.

## Attribution

- LibriSpeech corpus (V. Panayotov, G. Chen, D. Povey, S. Khudanpur, ICASSP 2015), distributed by OpenSLR, resource 12, licence CC BY 4.0. Audio is fetched at run time and is not part of this repository.
- Speaker model: WeSpeaker ResNet34-LM (VoxCeleb), CC BY 4.0, run with sherpa-onnx (Apache-2.0). ASR: Whisper base.en (MIT). See `NOTICE`.

## Files

- `docs/eval/summary.csv`: every number in the tables above.
- `docs/eval/det-<set>-<condition>.csv`: 201 points of the DET curve per set and condition.
