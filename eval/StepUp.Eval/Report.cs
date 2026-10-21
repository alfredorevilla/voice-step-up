using System.Globalization;
using System.Text;
using StepUp.Core;
using StepUp.Verifier.Sherpa;

namespace StepUp.Eval;

public sealed record ReportInput(
    SetResult Dev,
    SetResult Test,
    Frozen Frozen,
    IReadOnlyDictionary<string, ConditionSummary> DevSummaries,
    IReadOnlyDictionary<string, ConditionSummary> TestSummaries,
    string ProtocolCommit,
    string ModelId,
    int Resamples);

/// <summary>Writes docs/eval-report.md and the CSV files. Every figure comes from the computed results; nothing is typed in.</summary>
public static class Report
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Pct(double v) => double.IsNaN(v) ? "n/a" : (v * 100).ToString("0.00", Inv) + "%";

    public static string Num(double v, int digits = 3) => double.IsNaN(v) ? "n/a" : v.ToString("F" + digits, Inv);

    private static string Ci(Interval i) => $"[{Pct(i.Lo)}, {Pct(i.Hi)}]";

    public static string Build(ReportInput r)
    {
        var f = r.Frozen;
        var p0 = r.TestSummaries["P0"];
        var s0 = r.TestSummaries["S0"];
        var sb = new StringBuilder();

        sb.AppendLine("# Evaluation report (M6)");
        sb.AppendLine();
        sb.AppendLine("> **Speaker verification only.** These numbers describe the speaker-embedding score of the production code on LibriSpeech read speech. They are not the full false-reject rate of the product (ASR rejects are not included), they say nothing about spoofing, cloning, splicing or browser audio, and the test set is small. See [Not measured](#not-measured) and [Limits](#limits).");
        sb.AppendLine();
        sb.AppendLine($"Protocol: `docs/design/evaluation-protocol.md`, committed as `{r.ProtocolCommit}` before any result existed. Reproduce with `scripts/eval.sh`.");
        sb.AppendLine();

        sb.AppendLine("## Headline (test-clean, primary condition P0)");
        sb.AppendLine();
        sb.AppendLine($"At the default threshold {Num(f.Threshold, 2)} (chosen on dev-clean, frozen before test-clean was scored):");
        sb.AppendLine();
        sb.AppendLine($"- False accept rate **{Pct(p0.AtDefault.Far)}** ({p0.AtDefault.FalseAccepts} of {p0.ImpostorTrials} impostor trials); Wilson 95% {Ci(p0.AtDefault.FarWilson)}; speaker-level bootstrap 95% {Ci(p0.AtDefault.FarBootstrap)}.");
        sb.AppendLine($"- False reject rate **{Pct(p0.AtDefault.Frr)}** ({p0.AtDefault.FalseRejects} of {p0.GenuineTrials} genuine trials); Wilson 95% {Ci(p0.AtDefault.FrrWilson)}; speaker-level bootstrap 95% {Ci(p0.AtDefault.FrrBootstrap)}.");
        sb.AppendLine($"- Equal error rate {Pct(p0.Eer)}; bootstrap 95% {Ci(p0.EerBootstrap)}.");
        sb.AppendLine($"- Best case (S0, full utterances): FAR {Pct(s0.AtDefault.Far)}, FRR {Pct(s0.AtDefault.Frr)}, EER {Pct(s0.Eer)}.");
        sb.AppendLine($"- Speakers: {p0.Speakers}. Distinct impostor speaker pairs: {p0.ImpostorSpeakerPairs}.");
        sb.AppendLine();

        sb.AppendLine("## Setup");
        sb.AppendLine();
        sb.AppendLine("| Item | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Speaker model id | `{r.ModelId}` |");
        sb.AppendLine($"| Code path | `SherpaVerifier.AnalyzeAsync` (quality gates and embedding), `Cosine.NormalizedMean`, `Cosine.Similarity`, `PhraseGenerator.Similarity` |");
        sb.AppendLine($"| Seed | {Design.MasterSeed} |");
        sb.AppendLine($"| dev-clean | {r.Dev.Speakers.Count} speakers, used only to choose the operating values |");
        sb.AppendLine($"| test-clean | {r.Test.Speakers.Count} speakers, scored once at the frozen values |");
        sb.AppendLine($"| Per speaker | enrollment pool {Design.EnrollmentPool} utterances, probe pool {Design.ProbePool} utterances, {Design.Repetitions} enrollments of {Design.SegmentsPerEnrollment} segments |");
        sb.AppendLine($"| Segment redraws after a quality-gate refusal | dev {r.Dev.SegmentRedraws}, test {r.Test.SegmentRedraws} |");
        sb.AppendLine($"| Bootstrap | speaker-level cluster bootstrap, {r.Resamples} resamples, percentile 95% |");
        sb.AppendLine();
        sb.AppendLine("Conditions:");
        sb.AppendLine();
        foreach (var (id, text) in Design.Descriptions)
        {
            sb.AppendLine($"- **{id}**: {text}");
        }

        sb.AppendLine();

        sb.AppendLine("## Values frozen on dev-clean");
        sb.AppendLine();
        sb.AppendLine("| Value | Rule | Result | Applied in |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine($"| Default threshold | R1: FAR 1% point in P0 on dev-clean, rounded up to 2 decimals | **{Num(f.Threshold, 2)}** | `StepUp:ThresholdSeed` |");
        sb.AppendLine($"| Strict threshold | R2: FAR 0.1% point, same rounding | {Num(f.StrictThreshold, 2)} | reported only |");
        sb.AppendLine($"| Enrollment consistency floor | R3: 1st percentile of the lowest pairwise cosine of genuine enrollments | **{Num(f.ConsistencyFloor, 2)}** | `StepUp:EnrollmentConsistencyFloor` |");
        sb.AppendLine($"| Phrase match threshold | R4 | **{(double.IsNaN(f.PhraseThreshold) ? "none qualifies" : Num(f.PhraseThreshold, 2))}** | `PhraseGenerator.MatchThreshold` |");
        sb.AppendLine($"| Cohort score normalisation | R5: adopt if P0 EER is 20% lower and no degraded EER rises | **{(f.CohortAdopted ? "criterion met" : "not adopted")}** | scores stay raw cosines |");
        sb.AppendLine();
        sb.AppendLine("Cohort normalisation, EER on dev-clean:");
        sb.AppendLine();
        sb.AppendLine("| Condition | Raw cosine | Cohort-normalised |");
        sb.AppendLine("|---|---|---|");
        foreach (var c in new[] { "P0", "P1", "P2", "P3", "P4" })
        {
            sb.AppendLine($"| {c} | {Pct(f.RawEer[c])} | {Pct(f.CohortEer[c])} |");
        }

        sb.AppendLine();
        if (f.CohortAdopted)
        {
            sb.AppendLine("The criterion was met. Shipping it is a separate change (a stored cohort set with its own id in `ModelId` and `Attempts`, and a licence note). It is recommended but not part of M6; scores in this report and in the service are raw cosines.");
        }
        else
        {
            sb.AppendLine("The criterion was not met, so nothing changes: scores are raw cosines.");
        }

        sb.AppendLine();

        sb.AppendLine("## Results on test-clean");
        sb.AppendLine();
        sb.AppendLine($"Threshold {Num(f.Threshold, 2)} (default) and {Num(f.StrictThreshold, 2)} (strict). Each rate is followed by its Wilson 95% interval, which treats trials as independent and so **understates** the uncertainty, and by the speaker-level bootstrap 95% interval.");
        sb.AppendLine();
        AppendResultTable(sb, r.TestSummaries, f.Threshold, s => s.AtDefault);
        sb.AppendLine();
        AppendResultTable(sb, r.TestSummaries, f.StrictThreshold, s => s.AtStrict);
        sb.AppendLine();
        sb.AppendLine("| Condition | Genuine trials | Impostor trials | Speakers | Impostor speaker pairs | Probes not acquired | Enrollments not acquired | EER | EER bootstrap 95% |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var c in Design.Conditions)
        {
            var s = r.TestSummaries[c];
            sb.AppendLine($"| {c} | {s.GenuineTrials} | {s.ImpostorTrials} | {s.Speakers} | {s.ImpostorSpeakerPairs} | {s.ProbesNotAcquired} of {s.ProbesAttempted} | {s.EnrollmentsNotAcquired} of {s.EnrollmentsAttempted} | {Pct(s.Eer)} | {Ci(s.EerBootstrap)} |");
        }

        sb.AppendLine();
        sb.AppendLine("A probe or enrollment the production quality gates refuse is a failure to acquire: the product asks the user to try again, so it is excluded from FAR and FRR and counted here.");
        sb.AppendLine();
        foreach (var c in new[] { "P1", "P2", "P3", "P4" })
        {
            var s = r.TestSummaries[c];
            if (s.ProbesNotAcquired == 0)
            {
                continue;
            }

            var all = s.ProbesNotAcquired == s.ProbesAttempted;
            sb.AppendLine(all
                ? $"- **{c}: the production gate refused every probe** ({s.ProbesNotAcquired} of {s.ProbesAttempted}), so this condition has no trials and no error rates. The gate refuses audio under {AudioQuality.MinSnrDb:0} dB signal-to-noise (`Noisy`), which is the intended behaviour; it also means the speaker model's robustness at this noise level is not measured by this run."
                : $"- **{c}**: the production gate refused {s.ProbesNotAcquired} of {s.ProbesAttempted} probes ({Pct((double)s.ProbesNotAcquired / s.ProbesAttempted)}); the rates above are for the probes that passed it.");
        }

        sb.AppendLine();

        AppendNist(sb, p0, f);
        AppendPhrase(sb, r);
        AppendEnrollment(sb, r);

        sb.AppendLine("## dev-clean at the frozen values (for reference)");
        sb.AppendLine();
        AppendResultTable(sb, r.DevSummaries, f.Threshold, s => s.AtDefault);
        sb.AppendLine();
        sb.AppendLine("These rates were used to choose the values, so they are optimistic by construction. Only the test-clean table above is a result.");
        sb.AppendLine();

        sb.AppendLine("## Not measured");
        sb.AppendLine();
        sb.AppendLine("- The full false-reject rate: speaker rejects plus ASR phrase rejects together. Only the speaker score is evaluated here.");
        sb.AppendLine("- Spoofing: replayed recordings, synthetic or cloned voices, and words spliced from a victim's recordings.");
        sb.AppendLine("- Browser capture mismatch: echo cancellation, noise suppression, automatic gain, resampler quality, and a real microphone.");
        sb.AppendLine("- Session variability: illness, mood, time of day and different devices between enrollment and probe.");
        sb.AppendLine();

        sb.AppendLine("## Limits");
        sb.AppendLine();
        sb.AppendLine($"- Read English audiobook speech from {r.Dev.Speakers.Count} + {r.Test.Speakers.Count} speakers: too few speakers for narrow intervals, and trials drawn from them are correlated.");
        sb.AppendLine("- Genuine probes come from the same recording sessions as the enrollment audio (LibriSpeech chapters), so genuine scores are optimistic.");
        sb.AppendLine("- Noise is synthetic white noise and the band limit is a filter; neither is real room or telephone audio.");
        sb.AppendLine("- The 1% FAR operating point is a demonstrator choice, not a conformance claim. Scores are cosine similarities, not probabilities, and this report does not call them calibrated.");
        sb.AppendLine("- The Wilson intervals assume independent trials and are lower bounds on the uncertainty. The bootstrap resamples claimed speakers; dependence through impostor-side speakers remains.");
        sb.AppendLine();

        sb.AppendLine("## Deviations from the protocol");
        sb.AppendLine();
        sb.AppendLine("- Percentiles use linear interpolation between ranks (the protocol did not name a method).");
        sb.AppendLine("- Phrase cases the production quality gate refuses are excluded from the phrase rates and counted (the protocol did not say how to treat them).");
        sb.AppendLine("- Dependence of the genuine/impostor trials of one probe on many claimed speakers is not modelled beyond the cluster bootstrap on the claimed speaker.");
        sb.AppendLine();

        sb.AppendLine("## Attribution");
        sb.AppendLine();
        sb.AppendLine("- LibriSpeech corpus (V. Panayotov, G. Chen, D. Povey, S. Khudanpur, ICASSP 2015), distributed by OpenSLR, resource 12, licence CC BY 4.0. Audio is fetched at run time and is not part of this repository.");
        sb.AppendLine("- Speaker model: WeSpeaker ResNet34-LM (VoxCeleb), CC BY 4.0, run with sherpa-onnx (Apache-2.0). ASR: Whisper base.en (MIT). See `NOTICE`.");
        sb.AppendLine();

        sb.AppendLine("## Files");
        sb.AppendLine();
        sb.AppendLine("- `docs/eval/summary.csv`: every number in the tables above.");
        sb.AppendLine("- `docs/eval/det-<set>-<condition>.csv`: 201 points of the DET curve per set and condition.");
        return sb.ToString();
    }

    public static void WriteCsv(string directory, ReportInput r)
    {
        Directory.CreateDirectory(directory);
        var summary = new StringBuilder();
        summary.AppendLine("set,condition,threshold_kind,threshold,genuine_trials,impostor_trials,speakers,impostor_speaker_pairs,far,far_wilson_lo,far_wilson_hi,far_boot_lo,far_boot_hi,frr,frr_wilson_lo,frr_wilson_hi,frr_boot_lo,frr_boot_hi,eer,eer_boot_lo,eer_boot_hi,probes_not_acquired,probes_attempted,enrollments_not_acquired,enrollments_attempted");
        foreach (var (set, summaries) in new[] { ("dev-clean", r.DevSummaries), ("test-clean", r.TestSummaries) })
        {
            foreach (var c in Design.Conditions)
            {
                var s = summaries[c];
                foreach (var (kind, point) in new[] { ("default", s.AtDefault), ("strict", s.AtStrict) })
                {
                    summary.AppendLine(string.Join(',', new[]
                    {
                        set, c, kind, Num(point.Threshold, 2), s.GenuineTrials.ToString(Inv), s.ImpostorTrials.ToString(Inv), s.Speakers.ToString(Inv), s.ImpostorSpeakerPairs.ToString(Inv),
                        Num(point.Far, 6), Num(point.FarWilson.Lo, 6), Num(point.FarWilson.Hi, 6), Num(point.FarBootstrap.Lo, 6), Num(point.FarBootstrap.Hi, 6),
                        Num(point.Frr, 6), Num(point.FrrWilson.Lo, 6), Num(point.FrrWilson.Hi, 6), Num(point.FrrBootstrap.Lo, 6), Num(point.FrrBootstrap.Hi, 6),
                        Num(s.Eer, 6), Num(s.EerBootstrap.Lo, 6), Num(s.EerBootstrap.Hi, 6),
                        s.ProbesNotAcquired.ToString(Inv), s.ProbesAttempted.ToString(Inv), s.EnrollmentsNotAcquired.ToString(Inv), s.EnrollmentsAttempted.ToString(Inv),
                    }));
                }

                var det = new StringBuilder();
                det.AppendLine("threshold,far,frr");
                foreach (var d in s.Det)
                {
                    det.AppendLine($"{Num(d.Threshold, 6)},{Num(d.Far, 6)},{Num(d.Frr, 6)}");
                }

                File.WriteAllText(Path.Combine(directory, $"det-{set}-{c}.csv"), det.ToString());
            }
        }

        File.WriteAllText(Path.Combine(directory, "summary.csv"), summary.ToString());
    }

    private static void AppendResultTable(StringBuilder sb, IReadOnlyDictionary<string, ConditionSummary> summaries, double threshold, Func<ConditionSummary, OperatingPoint> pick)
    {
        sb.AppendLine($"Threshold {Num(threshold, 2)}:");
        sb.AppendLine();
        sb.AppendLine("| Condition | FAR | FAR Wilson | FAR bootstrap | FRR | FRR Wilson | FRR bootstrap |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in Design.Conditions)
        {
            var p = pick(summaries[c]);
            sb.AppendLine($"| {c} | {Pct(p.Far)} ({p.FalseAccepts}) | {Ci(p.FarWilson)} | {Ci(p.FarBootstrap)} | {Pct(p.Frr)} ({p.FalseRejects}) | {Ci(p.FrrWilson)} | {Ci(p.FrrBootstrap)} |");
        }
    }

    private static void AppendNist(StringBuilder sb, ConditionSummary p0, Frozen f)
    {
        var n = p0.ImpostorTrials;
        sb.AppendLine("## Comparison with NIST SP 800-63B-4");
        sb.AppendLine();
        sb.AppendLine("NIST SP 800-63B-4 asks for a false match rate of 1 in 10,000 or better and a false non-match rate under 5%. It also says biometric comparison based on voice SHALL NOT be used. This project is a demonstrator and does not claim to meet any of it.");
        sb.AppendLine();
        sb.AppendLine($"- Measured FAR at the default threshold in P0: {Pct(p0.AtDefault.Far)} with {n} impostor trials from {p0.Speakers} speakers; Wilson upper bound {Pct(p0.AtDefault.FarWilson.Hi)}, bootstrap upper bound {Pct(p0.AtDefault.FarBootstrap.Hi)}. The target is 0.01%.");
        sb.AppendLine($"- Zero errors in n independent trials bounds the rate at about 3/n with 95% confidence, so 1 in 10,000 needs about 30,000 independent impostor trials. This run has {n} trials, but they are not independent: they are drawn from {p0.Speakers} speakers ({p0.ImpostorSpeakerPairs} speaker pairs). **No number of trials drawn from this few speakers can demonstrate a false match rate of 1 in 10,000.**");
        var frrVerdict = p0.AtDefault.FrrBootstrap.Hi < 0.05 ? "below" : "not shown to be below";
        sb.AppendLine($"- Measured FRR at the default threshold in P0: {Pct(p0.AtDefault.Frr)} (bootstrap upper bound {Pct(p0.AtDefault.FrrBootstrap.Hi)}): {frrVerdict} the 5% target on this read-speech set. This ignores ASR rejects and real-world variability.");
        sb.AppendLine($"- At the strict threshold {Num(f.StrictThreshold, 2)}: FAR {Pct(p0.AtStrict.Far)}, FRR {Pct(p0.AtStrict.Frr)}.");
        sb.AppendLine();
    }

    private static void AppendPhrase(StringBuilder sb, ReportInput r)
    {
        var f = r.Frozen;
        sb.AppendLine("## Phrase check tolerance (R4)");
        sb.AppendLine();
        sb.AppendLine($"Real read speech stands in for a spoken phrase: LibriSpeech utterances of {PhraseCheck.MinWords} to {PhraseCheck.MaxWords} words and {PhraseCheck.MinSeconds} to {PhraseCheck.MaxSeconds} s, the production Whisper transcript compared with the true text by `PhraseGenerator.Similarity`. A wrong phrase is another utterance's text; a near miss is the true text with one word replaced.");
        sb.AppendLine();
        sb.AppendLine("| Set | Candidates in range | Cases used | Refused by quality gate | Threshold | Correct accepted | Wrong accepted | Near miss accepted |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var (name, set) in new[] { ("dev-clean (chooses)", r.Dev), ("test-clean (reports)", r.Test) })
        {
            if (double.IsNaN(f.PhraseThreshold))
            {
                sb.AppendLine($"| {name} | {set.Phrase.Candidates} | {set.Phrase.Selected} | {set.Phrase.RejectedByGate} | none | n/a | n/a | n/a |");
                continue;
            }

            var rates = PhraseCheck.Rates(set.Phrase.Scores, f.PhraseThreshold);
            sb.AppendLine($"| {name} | {set.Phrase.Candidates} | {set.Phrase.Selected} | {set.Phrase.RejectedByGate} | {Num(f.PhraseThreshold, 2)} | {Pct(rates.CorrectAccepted)} | {Pct(rates.WrongAccepted)} | {Pct(rates.NearMissAccepted)} |");
        }

        sb.AppendLine();
        sb.AppendLine($"Rule: keep {Num(PhraseGenerator.MatchThreshold, 2)} if at least 90% of correct phrases and at most 1% of wrong phrases are accepted on dev-clean; otherwise the grid value described in the protocol.");
        sb.AppendLine("A near miss (one wrong word) is mostly accepted by a letter-similarity check; this is a known weakness of the check and is shown for information, not gated.");
        sb.AppendLine();
    }

    private static void AppendEnrollment(StringBuilder sb, ReportInput r)
    {
        var f = r.Frozen;
        double Below(IReadOnlyList<double> values) => values.Count == 0 ? double.NaN : (double)values.Count(v => v < f.ConsistencyFloor) / values.Count;
        sb.AppendLine("## Enrollment consistency floor (R3)");
        sb.AppendLine();
        sb.AppendLine("| Set | Genuine enrollments | Below the floor | Poisoned enrollments (2 + 1 foreign segment) | Below the floor |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var (name, set) in new[] { ("dev-clean", r.Dev), ("test-clean", r.Test) })
        {
            sb.AppendLine($"| {name} | {set.GenuineMinimumPairwise.Count} | {Pct(Below(set.GenuineMinimumPairwise))} | {set.PoisonedMinimumPairwise.Count} | {Pct(Below(set.PoisonedMinimumPairwise))} |");
        }

        sb.AppendLine();
        sb.AppendLine($"Floor {Num(f.ConsistencyFloor, 2)}, from the first percentile of genuine enrollments on dev-clean. A poisoned enrollment under the floor is refused; the rest pass, so this check catches some, not all, foreign samples.");
        sb.AppendLine();
    }
}
