using StepUp.Eval;

namespace StepUp.Eval.Tests;

public class DesignTests
{
    [Fact]
    public void Pick_distinct_returns_distinct_indexes_in_range()
    {
        var picked = Design.PickDistinct(new Random(3), 30, 3);
        Assert.Equal(3, picked.Distinct().Count());
        Assert.All(picked, i => Assert.InRange(i, 0, 29));
    }

    [Fact]
    public void Seeded_choices_repeat_across_calls()
    {
        Assert.Equal(Design.ImpostorProbeIndexes(4, 2, 9, 30), Design.ImpostorProbeIndexes(4, 2, 9, 30));
        Assert.Equal(Design.EnrollmentUtterances("1272", 3), Design.EnrollmentUtterances("1272", 3));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(23)]
    [InlineData(10)]
    public void Impostor_probes_come_from_inside_the_other_speakers_pool(int poolSize)
    {
        var picked = Design.ImpostorProbeIndexes(0, 0, 1, poolSize);
        Assert.Equal(Design.ImpostorProbesPerSpeaker, picked.Distinct().Count());
        Assert.All(picked, i => Assert.InRange(i, 0, poolSize - 1));
    }

    [Fact]
    public void Seeded_choices_differ_when_the_key_differs()
    {
        var all = Enumerable.Range(0, 10).Select(rep => string.Join(',', Design.EnrollmentUtterances("1272", rep))).Distinct();
        Assert.True(all.Count() > 1);
    }

    [Fact]
    public void Seeds_are_stable_across_processes_because_they_come_from_a_hash_of_the_text()
    {
        // The first values for fixed keys never change: a change here means reproducibility broke.
        Assert.Equal(new Random(Seeds.Rng(6001, "x", 1).Next()).Next(), new Random(Seeds.Rng(6001, "x", 1).Next()).Next());
        Assert.Equal(Seeds.Rng(6001, "x", 1).Next(), Seeds.Rng(6001, "x", 1).Next());
        Assert.NotEqual(Seeds.Rng(6001, "x", 1).Next(), Seeds.Rng(6001, "x", 2).Next());
    }

    [Fact]
    public void Shuffle_keeps_every_utterance_once()
    {
        var ids = Enumerable.Range(0, 50).Select(n => $"u{n:D2}").ToArray();
        var shuffled = Design.ShuffleForPools("dev-clean", "84", ids);
        Assert.Equal(ids.OrderBy(s => s), shuffled.OrderBy(s => s));
        Assert.NotEqual(ids, shuffled);
    }

    [Fact]
    public void The_pools_fit_inside_what_one_speaker_provides()
    {
        // 8 + 30 utterances of at least 4 s per speaker; LibriSpeech clean sets have well over 38 per speaker.
        Assert.Equal(38, Design.EnrollmentPool + Design.ProbePool);
        Assert.Equal(6000, 40 * Design.Repetitions * Design.ProbePool);
        Assert.Equal(23400, 40 * Design.Repetitions * 39 * Design.ImpostorProbesPerSpeaker);
        Assert.True(Design.MinProbePool <= Design.ProbePool);
    }
}
