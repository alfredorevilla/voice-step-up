using StepUp.Core;

namespace StepUp.Eval;

/// <summary>
/// One phrase case: the three letter similarities and, for the per-word rule, the transcript and the three expected texts
/// (LibriSpeech text and ASR output; no audio).
/// </summary>
public sealed record PhraseScores(
    string Id,
    double Correct,
    double Wrong,
    double NearMiss,
    string Transcript = "",
    string Own = "",
    string WrongText = "",
    string NearMissText = "");

public sealed record PhraseRates(int N, double CorrectAccepted, double WrongAccepted, double NearMissAccepted);

public sealed record PhraseRun(IReadOnlyList<PhraseScores> Scores, int Candidates, int Selected, int RejectedByGate, double WordsPerSecond = double.NaN);

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

    /// <summary>Amendment 2 (A2.4): the per-word floors tried, 0.50 to 0.70.</summary>
    public static readonly double[] WordGrid = [0.50, 0.55, 0.60, 0.65, 0.70];

    public const double WordRuleMinCorrectAccepted = 0.95;
    public const double WordRuleMaxNearMissAccepted = 0.05;
    public const double WordRuleMaxWrongAccepted = 0.0;

    public static PhraseRates Rates(IReadOnlyList<PhraseScores> scores, double threshold)
    {
        if (scores.Count == 0)
        {
            return new PhraseRates(0, double.NaN, double.NaN, double.NaN);
        }

        double Share(Func<PhraseScores, double> pick) => (double)scores.Count(s => pick(s) >= threshold) / scores.Count;
        return new PhraseRates(scores.Count, Share(s => s.Correct), Share(s => s.Wrong), Share(s => s.NearMiss));
    }

    /// <summary>Acceptance rates of the production per-word rule (<see cref="PhraseGenerator.Matches(string?, string, double)"/>) at floor <paramref name="wordFloor"/>.</summary>
    public static PhraseRates WordRuleRates(IReadOnlyList<PhraseScores> scores, double wordFloor)
    {
        if (scores.Count == 0)
        {
            return new PhraseRates(0, double.NaN, double.NaN, double.NaN);
        }

        double Share(Func<PhraseScores, string> text) =>
            (double)scores.Count(s => PhraseGenerator.Matches(s.Transcript, text(s), wordFloor)) / scores.Count;
        return new PhraseRates(scores.Count, Share(s => s.Own), Share(s => s.WrongText), Share(s => s.NearMissText));
    }

    /// <summary>
    /// A2.4: the smallest floor of <see cref="WordGrid"/> where correct phrases are accepted at least 95%, one-wrong-word phrases at most 5%
    /// and wrong whole phrases exactly 0%. NaN when none qualifies (the whole-phrase rule then stays alone).
    /// </summary>
    public static double ChooseWordFloor(IReadOnlyList<PhraseScores> scores)
    {
        foreach (var w in WordGrid)
        {
            var rates = WordRuleRates(scores, w);
            if (rates.N > 0
                && rates.CorrectAccepted >= WordRuleMinCorrectAccepted
                && rates.NearMissAccepted <= WordRuleMaxNearMissAccepted
                && rates.WrongAccepted <= WordRuleMaxWrongAccepted)
            {
                return w;
            }
        }

        return double.NaN;
    }

    /// <summary>One line per case, text only: id, transcript, the expected texts and the three similarities. For scoring the grid without audio.</summary>
    public static void WriteCases(string path, PhraseRun run)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, run.Scores.Select(s => string.Join('\t', s.Id, s.Transcript, s.Own, s.WrongText, s.NearMissText, s.Correct.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), s.Wrong.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), s.NearMiss.ToString("F4", System.Globalization.CultureInfo.InvariantCulture))));
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
        var wordCount = 0;
        var spokenSeconds = 0.0;
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
            var nearMiss = NearMiss(own, wrongText, rng);
            wordCount += own.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            spokenSeconds += (double)chosen[i].Samples.Length / Audio.SampleRate;
            scores.Add(new PhraseScores(
                chosen[i].Utterance.Id,
                PhraseGenerator.Similarity(transcript, own),
                PhraseGenerator.Similarity(transcript, wrongText),
                PhraseGenerator.Similarity(transcript, nearMiss),
                transcript,
                own,
                wrongText,
                nearMiss));
        }

        return new PhraseRun(scores, candidates.Length, chosen.Count, chosen.Count - scores.Count, spokenSeconds > 0 ? wordCount / spokenSeconds : double.NaN);
    }
}
