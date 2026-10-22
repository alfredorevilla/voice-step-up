using StepUp.Eval;

namespace StepUp.Eval.Tests;

public class ScoringTests
{
    // Embeddings are 2-D unit-ish vectors so the cosines can be worked out by hand.
    private static float[] V(double degrees) =>
        [(float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180)];

    private static SpeakerEmbeddings Speaker(int index, double baseDegrees, int probeCount = Design.ProbePool)
    {
        float[]?[][] grid(double offset) =>
            [.. Enumerable.Range(0, Design.Repetitions).Select(_ =>
                (float[]?[])[V(baseDegrees + offset), V(baseDegrees - offset), V(baseDegrees)])];

        var probes = Design.Conditions.ToDictionary(
            c => c,
            _ => (float[]?[])[.. Enumerable.Range(0, probeCount).Select(_ => (float[]?)V(baseDegrees + 5))]);
        return new SpeakerEmbeddings
        {
            Index = index,
            Id = $"s{index}",
            Primary = grid(10),
            Secondary = grid(10),
            Probes = probes,
            ProbeCount = probeCount,
        };
    }

    [Fact]
    public void Template_is_the_normalised_mean_and_missing_segments_give_none()
    {
        // 10 degrees up, 10 degrees down and 0: the mean direction is 0 degrees
        var template = Scoring.Template([V(10), V(-10), V(0)])!;
        Assert.Equal(1.0f, template[0], 5);
        Assert.Equal(0.0f, template[1], 5);
        Assert.Null(Scoring.Template([V(10), null, V(0)]));
    }

    [Fact]
    public void Genuine_and_impostor_counts_follow_the_design()
    {
        var speakers = new[] { Speaker(0, 0), Speaker(1, 90), Speaker(2, 180) };
        var trials = Scoring.Trials(speakers, "P0");

        var genuine = trials.Count(t => t.Genuine);
        var impostor = trials.Count(t => !t.Genuine);
        Assert.Equal(3 * Design.Repetitions * Design.ProbePool, genuine);
        Assert.Equal(3 * Design.Repetitions * 2 * Design.ImpostorProbesPerSpeaker, impostor);
        Assert.All(trials.Where(t => t.Genuine), t => Assert.Equal(t.Claimed, t.ProbeSpeaker));
        Assert.All(trials.Where(t => !t.Genuine), t => Assert.NotEqual(t.Claimed, t.ProbeSpeaker));
    }

    [Fact]
    public void Scores_are_cosines_of_template_and_probe()
    {
        var speakers = new[] { Speaker(0, 0), Speaker(1, 90) };
        var trials = Scoring.Trials(speakers, "P0");

        // template of speaker 0 points at 0 degrees; its probes at 5 degrees: cos 5 = 0.99619
        Assert.All(trials.Where(t => t.Genuine && t.Claimed == 0), t => Assert.Equal(Math.Cos(5 * Math.PI / 180), t.Score, 5));
        // speaker 1's probes at 95 degrees against the 0-degree template: cos 95 = -0.08716
        Assert.All(trials.Where(t => !t.Genuine && t.Claimed == 0), t => Assert.Equal(Math.Cos(95 * Math.PI / 180), t.Score, 5));
    }

    [Fact]
    public void A_speaker_with_a_smaller_probe_pool_gets_fewer_genuine_trials_and_is_sampled_inside_it()
    {
        var small = Speaker(0, 0, probeCount: 23);
        var large = Speaker(1, 90);
        var trials = Scoring.Trials([small, large], "P0");

        // speaker 0: 5 enrollments x its 23 probes; speaker 1: 5 enrollments x 30 probes
        Assert.Equal(Design.Repetitions * 23, trials.Count(t => t.Genuine && t.Claimed == 0));
        Assert.Equal(Design.Repetitions * 30, trials.Count(t => t.Genuine && t.Claimed == 1));
        // 3 impostor probes per enrollment from the other speaker, however large its pool
        Assert.Equal(Design.Repetitions * Design.ImpostorProbesPerSpeaker, trials.Count(t => !t.Genuine && t.Claimed == 1));
    }

    [Fact]
    public void A_probe_that_was_not_acquired_is_left_out_of_the_trials()
    {
        var a = Speaker(0, 0);
        var b = Speaker(1, 90);
        a.Probes["P3"][4] = null;

        var withGap = Scoring.Trials([a, b], "P3");
        var without = Scoring.Trials([a, b], "P0");

        Assert.Equal(without.Count(t => t.Genuine) - Design.Repetitions, withGap.Count(t => t.Genuine));
    }

    [Fact]
    public void An_enrollment_with_a_missing_segment_is_left_out()
    {
        var a = Speaker(0, 0);
        var b = Speaker(1, 90);
        a.Primary[2][1] = null;

        var trials = Scoring.Trials([a, b], "P0");

        Assert.Equal((Design.Repetitions - 1) * Design.ProbePool + Design.Repetitions * Design.ProbePool, trials.Count(t => t.Genuine));
    }

    [Fact]
    public void Secondary_condition_uses_the_full_utterance_enrollment()
    {
        var a = Speaker(0, 0);
        var b = Speaker(1, 90);
        // break the primary enrollments only: S0 must still produce trials
        for (var r = 0; r < Design.Repetitions; r++)
        {
            a.Primary[r][0] = null;
        }

        Assert.DoesNotContain(Scoring.Trials([a, b], "P0"), t => t.Claimed == 0);
        Assert.Contains(Scoring.Trials([a, b], "S0"), t => t.Claimed == 0);
    }

    [Fact]
    public void Minimum_pairwise_cosine_is_the_weakest_pair()
    {
        // vectors at 0, 10 and 40 degrees: pairs 10, 40 and 30 degrees apart -> the minimum is cos 40
        Assert.Equal(Math.Cos(40 * Math.PI / 180), Scoring.MinimumPairwise([V(0), V(10), V(40)]), 5);
    }

    [Fact]
    public void Poisoned_enrollments_have_a_lower_minimum_than_genuine_ones()
    {
        var speakers = new[] { Speaker(0, 0), Speaker(1, 90), Speaker(2, 180) };
        var genuine = Scoring.MinimumPairwise(speakers);
        var poisoned = Scoring.PoisonedMinimumPairwise(speakers);

        Assert.True(poisoned.Max() < genuine.Min());
    }

    [Fact]
    public void Cohort_normalisation_keeps_the_trial_set_and_centres_the_scores()
    {
        var speakers = new[] { Speaker(0, 0), Speaker(1, 60), Speaker(2, 120), Speaker(3, 180) };
        var raw = Scoring.Trials(speakers, "P0");
        var normalised = Scoring.Trials(speakers, "P0", cohortNormalise: true);

        Assert.Equal(raw.Count, normalised.Count);
        Assert.Equal(raw.Select(t => (t.Claimed, t.ProbeSpeaker, t.Genuine)), normalised.Select(t => (t.Claimed, t.ProbeSpeaker, t.Genuine)));
        Assert.NotEqual(raw.Select(t => t.Score), normalised.Select(t => t.Score));
    }
}
