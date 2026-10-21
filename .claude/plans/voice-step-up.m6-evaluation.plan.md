# Plan: voice-step-up, M6 FAR/FRR evaluation

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M6 FAR/FRR evaluation
**Complexity**: Medium
**Depends on**: M3
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
An evaluation tool that fetches LibriSpeech test-clean, generates seeded trials and degradations, computes FAR, FRR and EER with intervals, and publishes the report; default thresholds trace to it.

## Patterns to Mirror
Mirror the code written in the earlier milestones; the plan is re-grounded before execution.

## Files to Change
| Path | Action | Why |
|---|---|---|
| eval/StepUp.Eval | CREATE | Evaluation tool |
| docs/eval-report.md, docs/design/evaluation-protocol.md | CREATE | Report and pre-registered protocol |
| scripts/eval.sh, .github/workflows/eval.yml | CREATE | Reproduction and manual CI run |
| tests/StepUp.Eval.Tests | CREATE | Metric unit tests |

## Tasks
- **T6.1** `eval/StepUp.Eval`: fetch LibriSpeech `test-clean` from OpenSLR (md5 check) into a git-ignored folder; seeded trial lists; degradations (noise at set SNRs, 8 kHz band-limit). The primary condition matches the product: enroll from 3 segments of about 2-3 s combined with the production code, probe about 2 s; long clean clips are a secondary best-case condition. The evaluation calls the production embedding, averaging and cosine code, not its own copy (an equivalence check). Spoof set: TTS clips only if the TTS model licence is clean (checked first with Exa search and fetch of the model card); otherwise report spoofing as not measured. First write `docs/design/evaluation-protocol.md` fixing the trial design, conditions and the rule for choosing the operating point (for example the threshold where FAR is 1% on LibriSpeech `dev-clean`) before any result is seen. Thresholds, the enrollment-consistency floor and the cohort-normalisation decision are chosen on `dev-clean`; rates are reported at those frozen values on `test-clean`. State the cohort rule: adopt only if the protocol says so, then add a cohort-set id to `ModelId` and `Attempts`.
- **T6.2** Metrics: FAR, FRR, EER, DET points, Wilson 95% intervals plus a speaker-level bootstrap interval, with the speaker count and the number of distinct speaker pairs next to every interval (Wilson assumes independent trials, which impostor trials from 40 speakers are not); cohort mean score normalization experiment; unit tests with hand-computed values. Results are reported next to the NIST SP 800-63B-4 targets (false match rate of 1 in 10,000 or better, false non-match rate under 5%) with the number of impostor trials that would be needed to support the first (zero errors in n trials bounds the rate at about 3/n with 95% confidence, so about 30,000 trials; trials drawn from 40 speakers are correlated, so no number of them can support 1 in 10,000).
- **T6.3** Run and publish `docs/eval-report.md` plus CSV, with attribution and a "small test set" caveat; `scripts/eval.sh`; manual `eval.yml` workflow.
- **T6.4** Set default thresholds in configuration by applying the pre-registered rule on `dev-clean`; the report shows the rates at that frozen threshold on `test-clean`. The report labels its results "speaker verification only": the full false-reject rate (ASR rejects plus speaker rejects) and spoofing, splicing and browser-capture mismatch are listed as not measured.

## Validation
```bash
scripts/eval.sh
dotnet test tests/StepUp.Eval.Tests
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| Scores separate poorly, so thresholds look worse than expected | Medium | Cohort normalization experiment; report honestly |
| Dataset or TTS model licence is not clean | Low | Read the owner's page first (third-party-assets rule); otherwise report spoofing as not measured |
| The sample is too small to support the NIST false match rate | High | State it in the report (M6-A4) |

## Acceptance
- [x] M6-A1. `scripts/eval.sh` reproduces the report from a clean checkout (dataset fetched, checksums verified).
- [x] M6-A2. The report states FAR and FRR per threshold and per condition, with intervals and the number of trials.
- [x] M6-A3. Metric calculations are unit-tested against hand-computed examples.
- [x] M6-A4. The report states its limits: read speech, small speaker count, no real-world spoofing coverage, and that the sample is too small to demonstrate the NIST SP 800-63B-4 false match rate of 1 in 10,000.
- [x] M6-A5. Default thresholds in config trace to the report.

## Evidence and decisions

Protocol committed first (`f06f5e4`), amended once before any score existed (`de13bf3`, probe pool sized to the utterances a speaker has: 4 of 40 speakers in each set have fewer than 38 utterances of 4 s or more). Results: `docs/eval-report.md`, `docs/eval/`.

| Item | Evidence |
|---|---|
| M6-A1 | `scripts/eval.sh fetch` downloaded dev-clean and test-clean into an empty `eval-data/` and checked both MD5s against `eval/librispeech.md5`; `scripts/eval.sh run` produced the report. Recomputing 2 speakers per set without the cache gave bit-identical embeddings (840 values, maximum difference 0). Not repeated from a fresh clone; the manual `eval.yml` workflow does that on a runner. |
| M6-A2 | Report tables: FAR and FRR per condition at the default and strict thresholds with Wilson and speaker-bootstrap intervals and the trial counts. P3 has no trials: the production gate refused every probe (stated in the report). |
| M6-A3 | `tests/StepUp.Eval.Tests`: 91 tests, metric values worked out by hand in the test comments. Mutation check: flipping FAR `>=`, FRR `<`, the threshold boundary, the Wilson term and the percentile rank each turns a test red (one survivor, FRR `<=`, led to a new boundary test). |
| M6-A4 | Report sections "Not measured" and "Limits"; the NIST section says 40 speakers cannot demonstrate 1 in 10,000. |
| M6-A5 | `StepUp:ThresholdSeed` 0.80 and `StepUp:EnrollmentConsistencyFloor` 0.43 in `StepUpOptions` and `appsettings.json`, `PhraseGenerator.WordsPerPhrase` 6, from rules R1, R3 and R6 on dev-clean (condition L3E, Amendment 2); `ShippedDefaultsTests` pins code, `appsettings.json` and `docs/eval/summary.csv` together. They were 0.79, 0.46 and 4 words from the first report (condition P0). `PhraseGenerator.MatchThreshold` stays 0.60 (rule R4: 96.0% of correct and 0.0% of wrong phrases accepted on dev-clean). Cohort normalisation not adopted (P0 EER 8.44% to 7.51%, under the 20% bar). |

Findings for the owner (test-clean, primary condition P0, threshold 0.79):

- FAR 2.04% (bootstrap 1.23% to 2.93%), FRR 43.28% (37.84% to 48.51%), EER 10.69%. With a 2 s probe about 4 in 10 genuine attempts fall back to MFA. Full 10 s utterances reach EER 4.59%. A longer challenge phrase would help far more than any threshold; changing the phrase length is a design change (ADR-0002, T3.1) and is not made here.
- The same threshold gave FAR 0.68% on dev-clean and 2.04% on test-clean: a threshold chosen on 40 speakers does not carry over exactly. The threshold also does not carry over to another probe length (S0: FAR 5.74% at 0.79).
- The phrase check accepts a phrase with one wrong word 87.00% of the time (test-clean). It stops a replay of a different phrase, not a recording that differs by a word. This weakens the replay argument in ADR-0002 and TM-001 for partially matching audio; it is recorded here, not fixed.
- Noise: the production gate refuses all 5 dB probes and 11.7% of 10 dB probes; EER at 20 dB is 27.35% and at 10 dB 37.85%.
- The enrollment consistency floor catches 47.5% of poisoned enrollments (2 segments of one speaker and 1 of another) on test-clean.

Decisions made without a panel (a fork cannot convene one): the threshold, floor, phrase and cohort rules were pre-registered in the protocol and applied mechanically; the probe-pool amendment applies to every speaker alike and was committed before any score existed. Spoofing was not measured (scoped out in the protocol; the Piper voices are not clones of a target).

Out-of-scope fix: two M2 tests (`Assertion_RejectsHs256`, `Assertion_RejectsUnknownKid`) began to fail once the wall clock passed the fake clock's 12:00Z on 2026-10-21, because `CreateToken` stamped `nbf` with the real clock. The test helpers now pin `NotBefore` and `IssuedAt` to the fake clock.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.

## Amendment 2 evidence and decisions (2026-10-21)

Protocol: `docs/design/evaluation-protocol.md`, Amendment 2, committed (`632cb23`) before any new score; harness `6103f84`; results `docs/eval-report.md`, `docs/eval/`.

- Rule R6 (dev-clean) met: FRR(P0) 43.24% against FRR(L3E) 23.44%, a gain of 19.8 points against the required 10. The product moves to 6-word challenges and enrollment phrases, threshold 0.80, floor 0.43. On test-clean, scored once at the frozen values: L3E FAR 2.18%, FRR 23.82% (bootstrap 95% 19.53% to 28.34%), EER 7.67%. Eight words (L4E) not adopted: FRR 18.06% on dev-clean is not 10 points better than L3E.
- Per-word phrase rule: no floor from 0.50 to 0.70 met the gate on dev-clean; `StepUp:PhraseMinWordSimilarity` stays 0 (off), the whole-phrase rule at 0.60 stays alone. A phrase with one wrong word is still accepted 85.02% (dev-clean) and 87.00% (test-clean) of the time.
- Reproduction: P0 to P4 and S0 rows of `docs/eval/summary.csv` are byte-identical to the first report. The run printed 12 of 144 differences, all P3 (no trials; "n/a" vs "NaN" in the check, not in the data); fixed in `Reproduction.Compare` with a test (`8e6a93e`). The evaluation was not run again.
- Decided by a four-person panel (speaker-verification, architecture, security and product views) and then mechanically by the pre-registered rules; phrase length moved only after the longer-probe re-run met R6.
- Not measured: spoken challenge words (the seconds per word is an assumption, 1.89 words per second on dev-clean in running speech), splicing, TTS, and the poisoned-enrollment rate of the 0.43 floor.
