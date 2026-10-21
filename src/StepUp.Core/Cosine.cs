namespace StepUp.Core;

public static class Cosine
{
    public static double Similarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            throw new ArgumentException("Embeddings must have the same non-zero length.");
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }

        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>
    /// The lowest cosine between any two of the embeddings, or <see cref="double.MaxValue"/> when there are fewer than two.
    /// Enrollment samples below the consistency floor are rejected (docs/architecture.md section 2.1, threat TM-003).
    /// </summary>
    public static double MinimumPairwise(IReadOnlyList<float[]> embeddings)
    {
        var lowest = double.MaxValue;
        for (var a = 0; a < embeddings.Count; a++)
        {
            for (var b = a + 1; b < embeddings.Count; b++)
            {
                lowest = Math.Min(lowest, Similarity(embeddings[a], embeddings[b]));
            }
        }

        return lowest;
    }

    /// <summary>True when every pair of the embeddings has a cosine at or above <paramref name="floor"/> (a pair exactly at the floor passes).</summary>
    public static bool AllPairsAtLeast(IReadOnlyList<float[]> embeddings, double floor) => MinimumPairwise(embeddings) >= floor;

    /// <summary>Each embedding L2-normalised, averaged, then normalised again (docs/architecture.md section 2.1).</summary>
    public static float[] NormalizedMean(IReadOnlyList<float[]> embeddings)
    {
        if (embeddings.Count == 0)
        {
            throw new ArgumentException("At least one embedding is required.", nameof(embeddings));
        }

        var sum = new double[embeddings[0].Length];
        foreach (var e in embeddings)
        {
            var norm = Math.Sqrt(e.Sum(x => (double)x * x));
            for (var i = 0; i < sum.Length; i++)
            {
                sum[i] += norm == 0 ? 0 : e[i] / norm;
            }
        }

        var length = Math.Sqrt(sum.Sum(x => x * x));
        return [.. sum.Select(x => length == 0 ? 0f : (float)(x / length))];
    }
}
