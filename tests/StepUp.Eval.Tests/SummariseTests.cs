using StepUp.Eval;

namespace StepUp.Eval.Tests;

public class SummariseTests
{
    private static float[] V(double degrees) =>
        [(float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180)];

    // Template direction `template`, every probe at `probe` degrees. Genuine score = cos(template - probe).
    private static SpeakerEmbeddings Speaker(int index, double template, double probe, int probeCount, int missingProbes = 0, bool missingEnrollment = false)
    {
        float[]?[][] grid() =>
            [.. Enumerable.Range(0, Design.Repetitions).Select(_ => (float[]?[])[V(template), V(template), V(template)])];

        var primary = grid();
        if (missingEnrollment)
        {
            primary[0][1] = null;
        }

        var probes = Design.Conditions.ToDictionary(
            c => c,
            _ => (float[]?[])[.. Enumerable.Range(0, probeCount).Select(i => i < missingProbes ? null : (float[]?)V(probe))]);
        return new SpeakerEmbeddings
        {
            Index = index,
            Id = $"s{index}",
            Primary = primary,
            Secondary = grid(),
            Probes = probes,
            ProbeCount = probeCount,
        };
    }

    private static readonly Frozen FrozenAtHalf = new(0.5, 0.9, 0.46, 0.6, false, new Dictionary<string, double>(), new Dictionary<string, double>());

    private static ConditionSummary Run()
    {
        // Claimed 0 (template 0 deg) accepts the probes of speakers 1 and 2 (both at 0 deg): all its impostor trials are false accepts.
        // Claimed 1 (90 deg) and claimed 2 (180 deg) reject every probe at 0 deg.
        var speakers = new[]
        {
            Speaker(0, 0, 0, probeCount: 4),
            Speaker(1, 90, 0, probeCount: 4, missingProbes: 1),
            Speaker(2, 180, 0, probeCount: 4, missingEnrollment: true),
        };
        var set = SetResult.Build("synthetic", speakers, new PhraseRun([], 0, 0, 0));
        return Analysis.Summarise(set, "P0", FrozenAtHalf, resamples: 3000);
    }

    [Fact]
    public void Acquisition_counts_and_speaker_pairs_are_reported_as_built()
    {
        var summary = Run();

        Assert.Equal(3, summary.Speakers);
        Assert.Equal(3 * 4, summary.ProbesAttempted);
        Assert.Equal(1, summary.ProbesNotAcquired);
        Assert.Equal(3 * Design.Repetitions, summary.EnrollmentsAttempted);
        Assert.Equal(1, summary.EnrollmentsNotAcquired);
        Assert.Equal(6, summary.ImpostorSpeakerPairs);
    }

    [Fact]
    public void Operating_point_counts_follow_the_scores()
    {
        var summary = Run();

        Assert.True(summary.AtDefault.FalseAccepts > 0);
        Assert.Equal(summary.ImpostorTrials / 3.0, summary.AtDefault.FalseAccepts, 1.0); // only claimed speaker 0 is accepted
    }

    [Fact]
    public void Intervals_are_bootstrapped_over_the_claimed_speaker()
    {
        // Grouped by claimed speaker the three FARs are 1, 0 and 0, so a draw of three copies of speaker 0 gives exactly 1.0
        // (probability 1/27 per draw). Grouped by probe speaker no group can reach it: the largest pooled FAR is 0.5.
        var summary = Run();

        Assert.Equal(1.0, summary.AtDefault.FarBootstrap.Hi, 9);
        Assert.Equal(0.0, summary.AtDefault.FarBootstrap.Lo, 9);
    }
}
