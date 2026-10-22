using StepUp.Core;

namespace StepUp.Eval;

public sealed record Trial(int Claimed, int ProbeSpeaker, bool Genuine, double Score);

/// <summary>Turns embeddings into scored trials with the production template and cosine code (protocol sections 2 and 3).</summary>
public static class Scoring
{
    /// <summary>The enrollment template, or null when a segment was not acquired.</summary>
    public static float[]? Template(float[]?[] segments) =>
        segments.Any(s => s is null) ? null : Cosine.NormalizedMean([.. segments.OfType<float[]>()]);

    public static float[]?[] Templates(SpeakerEmbeddings speaker, string condition) =>
        [.. (condition == "S0" ? speaker.Secondary : Design.UsesLongEnrollment(condition) ? speaker.Extended : speaker.Primary).Select(Template)];

    /// <summary>
    /// All genuine and impostor trials of one condition. With <paramref name="cohortNormalise"/> each score becomes
    /// (score - mean) / std over the probe's cosines with the primary templates of all speakers except the claimed
    /// speaker and the probe's speaker (protocol rule R5).
    /// </summary>
    public static IReadOnlyList<Trial> Trials(IReadOnlyList<SpeakerEmbeddings> speakers, string condition, bool cohortNormalise = false)
    {
        var cohort = cohortNormalise ? CohortTemplates(speakers) : [];
        var cache = new Dictionary<(int Speaker, int Probe), double[]>();
        var trials = new List<Trial>();

        foreach (var claimed in speakers)
        {
            var templates = Templates(claimed, condition);
            for (var rep = 0; rep < templates.Length; rep++)
            {
                if (templates[rep] is not { } template)
                {
                    continue;
                }

                for (var i = 0; i < claimed.ProbeCount; i++)
                {
                    Add(claimed, rep, claimed, i, genuine: true);
                }

                foreach (var other in speakers.Where(s => s.Index != claimed.Index))
                {
                    foreach (var i in Design.ImpostorProbeIndexes(claimed.Index, rep, other.Index, other.ProbeCount))
                    {
                        Add(claimed, rep, other, i, genuine: false);
                    }
                }

                void Add(SpeakerEmbeddings c, int r, SpeakerEmbeddings p, int probeIndex, bool genuine)
                {
                    if (p.Probes[condition][probeIndex] is not { } probe)
                    {
                        return;
                    }

                    var score = Cosine.Similarity(template, probe);
                    if (cohortNormalise)
                    {
                        score = Normalise(score, probe, c.Index, p, probeIndex, cohort, cache);
                    }

                    trials.Add(new Trial(c.Index, p.Index, genuine, score));
                }
            }
        }

        return trials;
    }

    /// <summary>For every enrollment with all three samples: the lowest pairwise cosine among them (rule R3).</summary>
    public static IReadOnlyList<double> MinimumPairwise(IReadOnlyList<SpeakerEmbeddings> speakers) =>
    [
        .. speakers.SelectMany(s => s.Primary)
            .Where(segments => segments.All(e => e is not null))
            .Select(segments => MinimumPairwise([.. segments.OfType<float[]>()])),
    ];

    public static IReadOnlyList<double> MinimumPairwiseExtended(IReadOnlyList<SpeakerEmbeddings> speakers) =>
    [
        .. speakers.SelectMany(s => s.Extended)
            .Where(segments => segments.All(e => e is not null))
            .Select(segments => MinimumPairwise([.. segments.OfType<float[]>()])),
    ];

    public static double MinimumPairwise(IReadOnlyList<float[]> embeddings) => Cosine.MinimumPairwise(embeddings);

    /// <summary>Enrollments of two segments of speaker A and one of a random other speaker B, as a poisoning attempt.</summary>
    public static IReadOnlyList<double> PoisonedMinimumPairwise(IReadOnlyList<SpeakerEmbeddings> speakers)
    {
        var result = new List<double>();
        foreach (var a in speakers)
        {
            for (var rep = 0; rep < a.Primary.Length; rep++)
            {
                var own = a.Primary[rep];
                if (own[0] is not { } first || own[1] is not { } second)
                {
                    continue;
                }

                var rng = Seeds.Rng(Design.MasterSeed, "poison", a.Index, rep);
                var others = speakers.Where(s => s.Index != a.Index).ToArray();
                var b = others[rng.Next(others.Length)];
                var foreign = b.Primary[rng.Next(b.Primary.Length)][rng.Next(Design.SegmentsPerEnrollment)];
                if (foreign is not null)
                {
                    result.Add(MinimumPairwise([first, second, foreign]));
                }
            }
        }

        return result;
    }

    private static List<(int Owner, float[] Template)> CohortTemplates(IReadOnlyList<SpeakerEmbeddings> speakers) =>
    [
        .. speakers.SelectMany(s => Templates(s, "P0").Where(t => t is not null).Select(t => (s.Index, t!))),
    ];

    private static double Normalise(
        double score,
        float[] probe,
        int claimed,
        SpeakerEmbeddings probeSpeaker,
        int probeIndex,
        List<(int Owner, float[] Template)> cohort,
        Dictionary<(int Speaker, int Probe), double[]> cache)
    {
        if (!cache.TryGetValue((probeSpeaker.Index, probeIndex), out var cosines))
        {
            cosines = [.. cohort.Select(c => Cosine.Similarity(c.Template, probe))];
            cache[(probeSpeaker.Index, probeIndex)] = cosines;
        }

        double sum = 0, sumSquares = 0;
        var count = 0;
        for (var k = 0; k < cohort.Count; k++)
        {
            if (cohort[k].Owner == claimed || cohort[k].Owner == probeSpeaker.Index)
            {
                continue;
            }

            sum += cosines[k];
            sumSquares += cosines[k] * cosines[k];
            count++;
        }

        var mean = sum / count;
        var std = Math.Sqrt(Math.Max(0.0, (sumSquares / count) - (mean * mean)));
        return std == 0 ? 0.0 : (score - mean) / std;
    }
}
