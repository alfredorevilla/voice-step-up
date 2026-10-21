using StepUp.Core;

namespace StepUp.Eval;

public sealed record PhraseScores(string Id, double Correct, double Wrong, double NearMiss);

public sealed record PhraseRates(int N, double CorrectAccepted, double WrongAccepted, double NearMissAccepted);

public sealed record PhraseRun(IReadOnlyList<PhraseScores> Scores, int Candidates, int Selected, int RejectedByGate);

/// <summary>
/// Protocol rule R4: how well the production letter-similarity phrase check tolerates real read speech.
/// Short LibriSpeech utterances stand in for a spoken challenge phrase; their transcripts are the ground truth.
/// </summary>
public static class PhraseCheck
{
    public const int MaxCases = 300;
    public const double MinSeconds = 1.5;
    public const double MaxSeconds = 4.0;
    public const int MinWords = 3;
    public const int MaxWords = 6;
    public const double MinCorrectAccepted = 0.90;
    public const double MaxWrongAccepted = 0.01;

    public static readonly double[] Grid = [.. Enumerable.Range(0, 10).Select(n => 0.50 + (0.05 * n))];

    public static PhraseRates Rates(IReadOnlyList<PhraseScores> scores, double threshold)
    {
        if (scores.Count == 0)
        {
            return new PhraseRates(0, double.NaN, double.NaN, double.NaN);
        }

        double Share(Func<PhraseScores, double> pick) => (double)scores.Count(s => pick(s) >= threshold) / scores.Count;
        return new PhraseRates(scores.Count, Share(s => s.Correct), Share(s => s.Wrong), Share(s => s.NearMiss));
    }

    /// <summary>Rule R4. Keep the current value if it meets both limits, otherwise the grid value with the most correct phrases among those with at most 1% wrong (ties to the higher). NaN when none qualifies.</summary>
    public static double ChooseThreshold(IReadOnlyList<PhraseScores> scores, double current)
    {
        var atCurrent = Rates(scores, current);
        if (atCurrent.CorrectAccepted >= MinCorrectAccepted && atCurrent.WrongAccepted <= MaxWrongAccepted)
        {
            return current;
        }

        var best = double.NaN;
        var bestCorrect = -1.0;
        foreach (var t in Grid)
        {
            var rates = Rates(scores, t);
            if (rates.WrongAccepted > MaxWrongAccepted)
            {
                continue;
            }

            if (rates.CorrectAccepted >= bestCorrect)
            {
                best = t;
                bestCorrect = rates.CorrectAccepted;
            }
        }

        return best;
    }

    /// <summary>One word of <paramref name="text"/> replaced by a word of <paramref name="other"/> that the text does not contain.</summary>
    public static string NearMiss(string text, string other, Random rng)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pool = other.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !words.Contains(w, StringComparer.OrdinalIgnoreCase)).ToArray();
        var replacement = pool.Length == 0 ? "xylophone" : pool[rng.Next(pool.Length)];
        words[rng.Next(words.Length)] = replacement;
        return string.Join(' ', words);
    }

    public static async Task<PhraseRun> RunAsync(IVerifier verifier, LibriSpeech corpus, string set, int parallelism, CancellationToken cancellationToken)
    {
        var candidates = corpus.Speakers().SelectMany(corpus.Utterances)
            .Where(u => u.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= MinWords and <= MaxWords)
            .ToArray();
        var order = Design.PickDistinct(Seeds.Rng(Design.MasterSeed, "phrase", set), candidates.Length, candidates.Length);

        var chosen = new List<(Utterance Utterance, float[] Samples)>();
        for (var next = 0; next < order.Length && chosen.Count < MaxCases; next += 32)
        {
            var batch = order.Skip(next).Take(32).Select(i => candidates[i]).ToArray();
            var audio = await Task.WhenAll(batch.Select(u => Task.Run(() => LibriSpeech.Decode(u.Path), cancellationToken))).ConfigureAwait(false);
            for (var k = 0; k < batch.Length && chosen.Count < MaxCases; k++)
            {
                var seconds = (double)audio[k].Length / Audio.SampleRate;
                if (seconds is >= MinSeconds and <= MaxSeconds)
                {
                    chosen.Add((batch[k], audio[k]));
                }
            }
        }

        var transcripts = new string?[chosen.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, chosen.Count),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (i, ct) =>
            {
                var result = await verifier.AnalyzeAsync(Audio.ToWav(chosen[i].Samples), string.Empty, ct).ConfigureAwait(false);
                transcripts[i] = result.Reject is null ? result.Transcript ?? string.Empty : null;
            }).ConfigureAwait(false);

        var scores = new List<PhraseScores>();
        for (var i = 0; i < chosen.Count; i++)
        {
            if (transcripts[i] is not { } transcript)
            {
                continue;
            }

            var rng = Seeds.Rng(Design.MasterSeed, "phrase-wrong", chosen[i].Utterance.Id);
            var others = Enumerable.Range(0, chosen.Count).Where(j => j != i && chosen[j].Utterance.Text != chosen[i].Utterance.Text).ToArray();
            var wrongText = chosen[others[rng.Next(others.Length)]].Utterance.Text;
            var own = chosen[i].Utterance.Text;
            scores.Add(new PhraseScores(
                chosen[i].Utterance.Id,
                PhraseGenerator.Similarity(transcript, own),
                PhraseGenerator.Similarity(transcript, wrongText),
                PhraseGenerator.Similarity(transcript, NearMiss(own, wrongText, rng))));
        }

        return new PhraseRun(scores, candidates.Length, chosen.Count, chosen.Count - scores.Count);
    }
}
