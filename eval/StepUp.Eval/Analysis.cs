using StepUp.Core;

namespace StepUp.Eval;

public sealed record Interval(double Lo, double Hi);

public sealed record OperatingPoint(
    double Threshold,
    double Far,
    int FalseAccepts,
    Interval FarWilson,
    Interval FarBootstrap,
    double Frr,
    int FalseRejects,
    Interval FrrWilson,
    Interval FrrBootstrap);

public sealed record DetPoint(double Threshold, double Far, double Frr);

public sealed record ConditionSummary(
    string Id,
    int Speakers,
    int GenuineTrials,
    int ImpostorTrials,
    int ImpostorSpeakerPairs,
    int ProbesAttempted,
    int ProbesNotAcquired,
    int EnrollmentsAttempted,
    int EnrollmentsNotAcquired,
    double Eer,
    Interval EerBootstrap,
    OperatingPoint AtDefault,
    OperatingPoint AtStrict,
    IReadOnlyList<DetPoint> Det);

/// <summary>One LibriSpeech set after embedding: speakers, scored trials per condition, and the side experiments.</summary>
public sealed class SetResult
{
    public required string Name { get; init; }

    public required IReadOnlyList<SpeakerEmbeddings> Speakers { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<Trial>> Trials { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<Trial>> CohortTrials { get; init; }

    public required PhraseRun Phrase { get; init; }

    public required IReadOnlyList<double> GenuineMinimumPairwise { get; init; }

    public required IReadOnlyList<double> PoisonedMinimumPairwise { get; init; }

    public int SegmentRedraws => Speakers.Sum(s => s.SegmentRedraws);

    public static SetResult Build(string name, IReadOnlyList<SpeakerEmbeddings> speakers, PhraseRun phrase)
    {
        var trials = new Dictionary<string, IReadOnlyList<Trial>>();
        var cohort = new Dictionary<string, IReadOnlyList<Trial>>();
        foreach (var condition in Design.Conditions)
        {
            trials[condition] = Scoring.Trials(speakers, condition);
            if (condition != "S0")
            {
                cohort[condition] = Scoring.Trials(speakers, condition, cohortNormalise: true);
            }
        }

        return new SetResult
        {
            Name = name,
            Speakers = speakers,
            Trials = trials,
            CohortTrials = cohort,
            Phrase = phrase,
            GenuineMinimumPairwise = Scoring.MinimumPairwise(speakers),
            PoisonedMinimumPairwise = Scoring.PoisonedMinimumPairwise(speakers),
        };
    }

    public static (IReadOnlyList<double> Genuine, IReadOnlyList<double> Impostor) Split(IReadOnlyList<Trial> trials) =>
        ([.. trials.Where(t => t.Genuine).Select(t => t.Score)], [.. trials.Where(t => !t.Genuine).Select(t => t.Score)]);
}

public static class Analysis
{
    public static Frozen FreezeOnDev(SetResult dev)
    {
        var (_, impostor) = SetResult.Split(dev.Trials["P0"]);
        var raw = new Dictionary<string, double>();
        var norm = new Dictionary<string, double>();
        foreach (var condition in new[] { "P0", "P1", "P2", "P3", "P4" })
        {
            var (g, i) = SetResult.Split(dev.Trials[condition]);
            raw[condition] = Metrics.Eer(g, i).Eer;
            var (gn, inn) = SetResult.Split(dev.CohortTrials[condition]);
            norm[condition] = Metrics.Eer(gn, inn).Eer;
        }

        return new Frozen(
            Freeze.DefaultThreshold(impostor, Freeze.DefaultFar),
            Freeze.DefaultThreshold(impostor, Freeze.StrictFar),
            Freeze.ConsistencyFloor(dev.GenuineMinimumPairwise),
            PhraseCheck.ChooseThreshold(dev.Phrase.Scores, PhraseGenerator.MatchThreshold),
            Freeze.CohortAdopted(raw, norm),
            raw,
            norm);
    }

    public static ConditionSummary Summarise(SetResult set, string condition, Frozen frozen, int resamples)
    {
        var trials = set.Trials[condition];
        var (genuine, impostor) = SetResult.Split(trials);
        var bySpeaker = trials.GroupBy(t => t.Claimed)
            .Select(g => new SpeakerScores(g.Key, [.. g.Where(t => t.Genuine).Select(t => t.Score)], [.. g.Where(t => !t.Genuine).Select(t => t.Score)]))
            .ToArray();

        var (eer, _) = Metrics.Eer(genuine, impostor);
        var eerCi = Bootstrap(bySpeaker, (g, i) => Metrics.Eer(g, i).Eer, resamples, 1);

        var probesAttempted = set.Speakers.Count * Design.ProbePool;
        var probesMissing = set.Speakers.Sum(s => s.Probes[condition].Count(e => e is null));
        var enrollments = set.Speakers.Sum(s => s.Primary.Length);
        var enrollmentsMissing = set.Speakers.Sum(s => Scoring.Templates(s, condition).Count(t => t is null));

        return new ConditionSummary(
            condition,
            set.Speakers.Count,
            genuine.Count,
            impostor.Count,
            trials.Where(t => !t.Genuine).Select(t => (t.Claimed, t.ProbeSpeaker)).Distinct().Count(),
            probesAttempted,
            probesMissing,
            enrollments,
            enrollmentsMissing,
            eer,
            eerCi,
            Point(genuine, impostor, bySpeaker, frozen.Threshold, resamples),
            Point(genuine, impostor, bySpeaker, frozen.StrictThreshold, resamples),
            Det(genuine, impostor));
    }

    private static OperatingPoint Point(IReadOnlyList<double> genuine, IReadOnlyList<double> impostor, SpeakerScores[] bySpeaker, double t, int resamples)
    {
        var falseAccepts = impostor.Count(s => s >= t);
        var falseRejects = genuine.Count(s => s < t);
        var (fl, fh) = Metrics.Wilson(falseAccepts, impostor.Count);
        var (rl, rh) = Metrics.Wilson(falseRejects, genuine.Count);
        return new OperatingPoint(
            t,
            Metrics.Far(impostor, t),
            falseAccepts,
            new Interval(fl, fh),
            Bootstrap(bySpeaker, (g, i) => Metrics.Far(i, t), resamples, 2),
            Metrics.Frr(genuine, t),
            falseRejects,
            new Interval(rl, rh),
            Bootstrap(bySpeaker, (g, i) => Metrics.Frr(g, t), resamples, 3));
    }

    private static Interval Bootstrap(SpeakerScores[] speakers, Func<IReadOnlyList<double>, IReadOnlyList<double>, double> statistic, int resamples, int salt)
    {
        var (lo, hi) = Metrics.ClusterBootstrap(speakers, statistic, resamples, Design.MasterSeed + salt);
        return new Interval(lo, hi);
    }

    private static List<DetPoint> Det(IReadOnlyList<double> genuine, IReadOnlyList<double> impostor)
    {
        if (genuine.Count == 0 || impostor.Count == 0)
        {
            return [];
        }

        var all = genuine.Concat(impostor).OrderBy(s => s).ToArray();
        var points = new List<DetPoint>();
        for (var k = 0; k <= 200; k++)
        {
            var t = all[Math.Min(all.Length - 1, k * (all.Length - 1) / 200)];
            points.Add(new DetPoint(t, Metrics.Far(impostor, t), Metrics.Frr(genuine, t)));
        }

        return points;
    }
}
