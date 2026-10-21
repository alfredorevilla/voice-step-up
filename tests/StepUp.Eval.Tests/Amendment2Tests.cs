using StepUp.Eval;

namespace StepUp.Eval.Tests;

public class Amendment2Tests
{
    private static float[] V(double degrees) =>
        [(float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180)];

    // ---- A2.3 (R6): adopt a 6-word challenge only for a 10-point FRR gain on dev-clean

    [Theory]
    [InlineData(0.50, 0.40, true)]
    [InlineData(0.50, 0.4001, false)]
    [InlineData(0.4324, 0.30, true)]
    [InlineData(0.50, 0.50, false)]
    [InlineData(0.30, 0.45, false)]
    [InlineData(double.NaN, 0.20, false)]
    [InlineData(0.50, double.NaN, false)]
    public void The_longer_challenge_needs_ten_points_lower_frr(double frrP0, double frrCandidate, bool adopted) =>
        Assert.Equal(adopted, Freeze.AdoptLongChallenge(frrP0, frrCandidate));

    // ---- A2.1: the conditions

    [Fact]
    public void The_new_conditions_exist_and_the_original_ones_are_unchanged()
    {
        Assert.Equal(["P0", "P1", "P2", "P3", "P4", "S0", "L3", "L3E", "L4", "L4E"], Design.Conditions);
        Assert.Equal(["L3", "L3E", "L4", "L4E"], Design.LongConditions);
        Assert.Equal(["P0", "P1", "P2", "P3", "P4"], Design.CohortConditions);
        Assert.All(Design.Conditions, c => Assert.True(Design.Descriptions.ContainsKey(c), c));
    }

    [Theory]
    [InlineData("P0", 2.0, false)]
    [InlineData("P4", 2.0, false)]
    [InlineData("L3", 3.0, false)]
    [InlineData("L3E", 3.0, true)]
    [InlineData("L4", 4.0, false)]
    [InlineData("L4E", 4.0, true)]
    public void Probe_and_enrollment_lengths_follow_the_protocol_table(string condition, double probeSeconds, bool longEnrollment)
    {
        Assert.Equal(probeSeconds, Design.ProbeSecondsFor(condition));
        Assert.Equal(longEnrollment, Design.UsesLongEnrollment(condition));
    }

    [Fact]
    public void The_longest_segment_fits_the_shortest_utterance_so_the_pools_and_the_original_conditions_stay_unchanged()
    {
        Assert.True(Design.Probe4Seconds <= Design.MinUtteranceSeconds);
        Assert.True(Design.LongEnrollmentSeconds <= Design.MinUtteranceSeconds);
    }

    // ---- scoring: L3 enrolls like P0, L3E from the 3.5 s grid, and both score the same probes

    private static SpeakerEmbeddings Speaker(int index, double shortDegrees, double longDegrees, double probeDegrees)
    {
        float[]?[][] grid(double d) => [.. Enumerable.Range(0, Design.Repetitions).Select(_ => (float[]?[])[V(d), V(d), V(d)])];
        var probes = Design.Conditions.ToDictionary(c => c, _ => (float[]?[])[V(probeDegrees), V(probeDegrees), V(probeDegrees), V(probeDegrees)]);
        return new SpeakerEmbeddings
        {
            Index = index,
            Id = $"s{index}",
            Primary = grid(shortDegrees),
            Secondary = grid(0),
            Extended = grid(longDegrees),
            Probes = probes,
            ProbeCount = 4,
        };
    }

    [Fact]
    public void The_short_variants_enroll_from_the_primary_grid_and_the_long_variants_from_the_extended_grid()
    {
        var s = Speaker(0, shortDegrees: 0, longDegrees: 60, probeDegrees: 0);

        Assert.Equal(0.0, Math.Acos(StepUp.Core.Cosine.Similarity(Scoring.Templates(s, "L3")[0]!, V(0))), 5);
        Assert.Equal(0.0, Math.Acos(StepUp.Core.Cosine.Similarity(Scoring.Templates(s, "L4")[0]!, V(0))), 5);
        Assert.Equal(60.0 * Math.PI / 180, Math.Acos(StepUp.Core.Cosine.Similarity(Scoring.Templates(s, "L3E")[0]!, V(0))), 5);
        Assert.Equal(60.0 * Math.PI / 180, Math.Acos(StepUp.Core.Cosine.Similarity(Scoring.Templates(s, "L4E")[0]!, V(0))), 5);
    }

    [Fact]
    public void Genuine_scores_of_a_long_variant_use_the_long_enrollment()
    {
        var speakers = new[] { Speaker(0, 0, 60, 0), Speaker(1, 90, 150, 90) };

        var shortScores = Scoring.Trials(speakers, "L3").Where(t => t.Genuine).Select(t => t.Score).Distinct().ToArray();
        var longScores = Scoring.Trials(speakers, "L3E").Where(t => t.Genuine).Select(t => t.Score).Distinct().ToArray();

        Assert.All(shortScores, v => Assert.Equal(1.0, v, 5));
        Assert.All(longScores, v => Assert.Equal(0.5, v, 5)); // cos 60 degrees
    }

    [Fact]
    public void The_minimum_pairwise_of_the_extended_enrollments_comes_from_the_extended_grid()
    {
        var s = Speaker(0, shortDegrees: 0, longDegrees: 0, probeDegrees: 0);
        s.Extended[0][1] = V(60); // one repetition has a segment 60 degrees away: cosine 0.5

        var minima = Scoring.MinimumPairwiseExtended([s]);

        Assert.Equal(Design.Repetitions, minima.Count);
        Assert.Equal(0.5, minima.Min(), 5);
        Assert.Equal(1.0, Scoring.MinimumPairwise([s]).Min(), 5); // the primary grid is untouched
    }

    // ---- A2.4: the per-word gate

    private static PhraseScores Case(string transcript, string own, string wrong, string near) => new("c", 0, 0, 0, transcript, own, wrong, near);

    [Fact]
    public void The_word_floor_is_the_smallest_one_meeting_all_three_limits()
    {
        // Every transcript is heard exactly. In 4 of 20 cases the near-miss word "claft" is 0.6 similar to "cliff" (two substitutions),
        // so floors up to 0.60 accept 20% of near misses (limit 5%); from 0.65 up none is accepted.
        const string Own = "cliff peach marble drum";
        var cases = Enumerable.Range(0, 20)
            .Select(i => Case(Own, Own, "orange violin ladder meadow", i < 4 ? "claft peach marble drum" : "qqqqq peach marble drum"))
            .ToArray();

        Assert.Equal(0.65, PhraseCheck.ChooseWordFloor(cases), 9);
        Assert.Equal(0.20, PhraseCheck.WordRuleRates(cases, 0.60).NearMissAccepted, 9);
        Assert.Equal(0.0, PhraseCheck.WordRuleRates(cases, 0.65).NearMissAccepted, 9);
    }

    [Fact]
    public void No_floor_is_chosen_when_correct_phrases_are_rejected_too_often()
    {
        // 5 of 20 correct phrases are heard with a wrong last word: rejected at every floor, so 75% < 95% correct accepted
        const string Own = "apple bridge candle river";
        var cases = Enumerable.Range(0, 20)
            .Select(i => Case(i < 5 ? "apple bridge candle table" : Own, Own, "orange violin ladder meadow", "apple bridge candle qqqqq"))
            .ToArray();

        Assert.True(double.IsNaN(PhraseCheck.ChooseWordFloor(cases)));
        Assert.Equal(0.75, PhraseCheck.WordRuleRates(cases, 0.50).CorrectAccepted, 9);
    }

    [Fact]
    public void No_floor_is_chosen_when_a_wrong_whole_phrase_gets_through()
    {
        const string Own = "apple bridge candle river";
        var cases = Enumerable.Range(0, 20)
            .Select(i => Case(Own, Own, i == 0 ? Own : "orange violin ladder meadow", "apple bridge candle qqqqq")) // case 0: the "wrong" text is identical
            .ToArray();

        Assert.True(double.IsNaN(PhraseCheck.ChooseWordFloor(cases)));
    }

    [Fact]
    public void No_floor_is_chosen_without_cases()
    {
        Assert.True(double.IsNaN(PhraseCheck.ChooseWordFloor([])));
    }

    // ---- extended cache fields

    [Fact]
    public void The_cache_round_trips_the_extended_grid_and_rejects_older_versions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stepup-eval-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new Cache(dir);
            var s = Speaker(3, 10, 70, 10);
            s.Extended[2][1] = null;

            cache.Write("dev-clean", s);
            Assert.True(cache.TryRead("dev-clean", "s3", out var read));

            Assert.NotNull(read);
            Assert.Null(read.Extended[2][1]);
            Assert.Equal(s.Extended[0][0], read.Extended[0][0]);
            Assert.Equal(Design.Conditions.Length, read.Probes.Count);

            // a cache file written by version 2 (before the extended grid) must be ignored
            using (var w = new BinaryWriter(File.Create(cache.PathFor("dev-clean", "old"))))
            {
                w.Write(2);
            }

            Assert.False(cache.TryRead("dev-clean", "old", out _));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---- A2.5: reproduction of the first report

    private static ConditionSummary Summary(string id, double far, double frr, double eer, int genuine, int impostor)
    {
        var interval = new Interval(0, 1);
        var point = new OperatingPoint(0.79, far, 0, interval, interval, frr, 0, interval, interval);
        return new ConditionSummary(id, 40, genuine, impostor, 1560, 0, 0, 0, 0, eer, interval, point, point, []);
    }

    private const string Header = "set,condition,threshold_kind,threshold,genuine_trials,impostor_trials,speakers,impostor_speaker_pairs,far,far_wilson_lo,far_wilson_hi,far_boot_lo,far_boot_hi,frr,frr_wilson_lo,frr_wilson_hi,frr_boot_lo,frr_boot_hi,eer,eer_boot_lo,eer_boot_hi,probes_not_acquired,probes_attempted,enrollments_not_acquired,enrollments_attempted";

    private static string Row(string set, string condition, string kind, string far, string frr, string eer) =>
        $"{set},{condition},{kind},0.79,5935,23400,40,1560,{far},0,0,0,0,{frr},0,0,0,0,{eer},0,0,0,0,0,0";

    [Fact]
    public void Reproduction_reports_an_exact_match_and_names_a_difference()
    {
        var summaries = new Dictionary<string, ConditionSummary> { ["P0"] = Summary("P0", 0.006752, 0.432350, 0.084408, 5935, 23400) };
        var same = Header + "\n" + Row("dev-clean", "P0", "default", "0.006752", "0.432350", "0.084408") + "\n";
        var different = Header + "\n" + Row("dev-clean", "P0", "default", "0.006752", "0.432351", "0.084408") + "\n";

        Assert.Contains("reproduce the first report exactly", Reproduction.Compare(same, summaries, summaries));
        var message = Reproduction.Compare(different, summaries, summaries);
        Assert.Contains("do NOT reproduce", message);
        Assert.Contains("FRR: 0.432351 before, 0.432350 now", message);
    }

    [Fact]
    public void Reproduction_reads_a_missing_score_written_as_n_a_as_a_match_and_still_flags_a_real_number()
    {
        var summaries = new Dictionary<string, ConditionSummary> { ["P3"] = Summary("P3", double.NaN, double.NaN, double.NaN, 5935, 23400) };
        var unscored = Header + "\n" + Row("dev-clean", "P3", "default", "n/a", "n/a", "n/a") + "\n";
        var scored = Header + "\n" + Row("dev-clean", "P3", "default", "0.006752", "n/a", "n/a") + "\n";

        Assert.Contains("reproduce the first report exactly", Reproduction.Compare(unscored, summaries, summaries));
        Assert.Contains("FAR: 0.006752 before, n/a now", Reproduction.Compare(scored, summaries, summaries));
    }

    [Fact]
    public void Reproduction_ignores_new_conditions_and_says_so_when_there_is_no_earlier_file()
    {
        var summaries = new Dictionary<string, ConditionSummary>
        {
            ["P0"] = Summary("P0", 0.006752, 0.432350, 0.084408, 5935, 23400),
            ["L3"] = Summary("L3", 0.5, 0.5, 0.5, 1, 1),
        };
        var csv = Header + "\n" + Row("dev-clean", "P0", "default", "0.006752", "0.432350", "0.084408") + "\n" + Row("dev-clean", "L3", "default", "0.0", "0.0", "0.0") + "\n";

        Assert.Contains("6 values compared", Reproduction.Compare(csv, summaries, summaries));
        Assert.Contains("was not checked", Reproduction.Compare(null, summaries, summaries));
    }
}
