using System.Security.Cryptography;
using System.Text;

namespace StepUp.Eval;

/// <summary>Genuine and impostor scores of the trials whose claimed (enrolled) speaker is <see cref="Speaker"/>.</summary>
public sealed record SpeakerScores(int Speaker, double[] Genuine, double[] Impostor);

/// <summary>Error-rate arithmetic of docs/design/evaluation-protocol.md section 5.</summary>
public static class Metrics
{
    public static double Far(IReadOnlyList<double> impostor, double threshold) =>
        impostor.Count == 0 ? double.NaN : (double)impostor.Count(s => s >= threshold) / impostor.Count;

    public static double Frr(IReadOnlyList<double> genuine, double threshold) =>
        genuine.Count == 0 ? double.NaN : (double)genuine.Count(s => s < threshold) / genuine.Count;

    /// <summary>
    /// The smallest threshold at which at most <paramref name="targetFar"/> of the impostor scores are at or above it.
    /// Sits just above the score that would be the first excess false accept.
    /// </summary>
    public static double ThresholdAtFar(IReadOnlyList<double> impostor, double targetFar)
    {
        if (impostor.Count == 0)
        {
            throw new ArgumentException("No impostor scores.", nameof(impostor));
        }

        var descending = impostor.OrderByDescending(s => s).ToArray();
        var allowed = (int)Math.Floor((targetFar * descending.Length) + 1e-9);
        return allowed >= descending.Length ? descending[^1] : Math.BitIncrement(descending[allowed]);
    }

    /// <summary>The point where FAR and FRR are closest; reported as their mean.</summary>
    public static (double Eer, double Threshold) Eer(IReadOnlyList<double> genuine, IReadOnlyList<double> impostor)
    {
        var g = genuine.OrderBy(s => s).ToArray();
        var i = impostor.OrderBy(s => s).ToArray();
        var candidates = g.Concat(i).Distinct().OrderBy(s => s).ToList();
        candidates.Add(Math.BitIncrement(candidates[^1]));

        var best = (Eer: double.NaN, Threshold: double.NaN);
        var bestGap = double.MaxValue;
        foreach (var t in candidates)
        {
            var frr = (double)LowerBound(g, t) / g.Length;
            var far = 1.0 - ((double)LowerBound(i, t) / i.Length);
            var gap = Math.Abs(far - frr);
            if (gap < bestGap)
            {
                bestGap = gap;
                best = ((far + frr) / 2.0, t);
            }
        }

        return best;
    }

    /// <summary>Wilson score interval for k of n. Trials are not independent, so this understates the uncertainty.</summary>
    public static (double Lo, double Hi) Wilson(int k, int n, double z = 1.96)
    {
        if (n == 0)
        {
            return (double.NaN, double.NaN);
        }

        var p = (double)k / n;
        var z2 = z * z;
        var denominator = 1.0 + (z2 / n);
        var centre = p + (z2 / (2.0 * n));
        var half = z * Math.Sqrt((p * (1.0 - p) / n) + (z2 / (4.0 * n * n)));
        return (Math.Max(0.0, (centre - half) / denominator), Math.Min(1.0, (centre + half) / denominator));
    }

    public static double RoundUp2(double value) => Math.Ceiling((value * 100.0) - 1e-9) / 100.0;

    public static double RoundDown2(double value) => Math.Floor((value * 100.0) + 1e-9) / 100.0;

    /// <summary>Linear interpolation between ranks (the NumPy default). <paramref name="p"/> is in [0, 1].</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("No values.", nameof(values));
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var rank = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + ((rank - lower) * (sorted[upper] - sorted[lower]));
    }

    /// <summary>
    /// Speaker-level cluster bootstrap: claimed speakers are resampled with replacement, carrying their genuine and
    /// impostor trials. Percentile 95% interval of <paramref name="statistic"/> over <paramref name="resamples"/> draws.
    /// </summary>
    public static (double Lo, double Hi) ClusterBootstrap(
        IReadOnlyList<SpeakerScores> speakers,
        Func<IReadOnlyList<double>, IReadOnlyList<double>, double> statistic,
        int resamples,
        int seed)
    {
        var rng = new Random(seed);
        var values = new List<double>(resamples);
        for (var b = 0; b < resamples; b++)
        {
            var genuine = new List<double>();
            var impostor = new List<double>();
            for (var n = 0; n < speakers.Count; n++)
            {
                var pick = speakers[rng.Next(speakers.Count)];
                genuine.AddRange(pick.Genuine);
                impostor.AddRange(pick.Impostor);
            }

            var value = statistic(genuine, impostor);
            if (!double.IsNaN(value))
            {
                values.Add(value);
            }
        }

        return (Percentile(values, 0.025), Percentile(values, 0.975));
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}

/// <summary>Stable seeds: <see cref="HashCode"/> and string.GetHashCode change between processes, so they are not used.</summary>
public static class Seeds
{
    public static Random Rng(int master, params object[] parts)
    {
        var text = string.Join('|', parts.Prepend(master));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new Random(BitConverter.ToInt32(hash, 0));
    }
}
