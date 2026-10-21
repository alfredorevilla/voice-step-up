using System.Security.Cryptography;

namespace StepUp.Core;

public static partial class PhraseGenerator
{
    public const int WordsPerPhrase = 4;

    /// <summary>Four challenge words drawn with a CSPRNG, lowercase, space separated.</summary>
    public static string Create() => Draw(Wordlist.Words);

    /// <summary>Four words from the separate enrollment list, so enrollment audio never contains a challenge word.</summary>
    public static string CreateEnrollment() => Draw(EnrollmentWordlist.Words);

    private static string Draw(string[] words)
    {
        var chosen = new string[WordsPerPhrase];
        for (var i = 0; i < chosen.Length; i++)
        {
            chosen[i] = words[RandomNumberGenerator.GetInt32(words.Length)];
        }

        return string.Join(' ', chosen);
    }

    /// <summary>Letter similarity at or above this counts as the right phrase (docs/design/verifier.md section 5).</summary>
    public const double MatchThreshold = 0.60;

    /// <summary>Lowercase, letters and digits only, single spaces. Used to compare a transcript with a phrase.</summary>
    public static string Normalize(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', new string([.. chars]).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// 1 - Levenshtein / longer length over the letters of both texts, spaces removed. Whisper tags such as
    /// "[ Silence ]" and "(Bell)" are dropped first. ASR often splits or merges isolated nouns, so words are not compared.
    /// </summary>
    public static double Similarity(string transcript, string phrase)
    {
        var a = Letters(transcript);
        var b = Letters(phrase);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return 1.0 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
    }

    public static bool Matches(string? transcript, string phrase) => Matches(transcript, phrase, minWordSimilarity: 0.0);

    /// <summary>
    /// The whole-phrase letter similarity must reach <see cref="MatchThreshold"/>. When <paramref name="minWordSimilarity"/> is
    /// above zero, every expected word must also reach it (<see cref="WordSimilarities"/>). Zero is the whole-phrase rule alone.
    /// </summary>
    public static bool Matches(string? transcript, string phrase, double minWordSimilarity)
    {
        if (string.IsNullOrWhiteSpace(transcript) || Similarity(transcript, phrase) < MatchThreshold)
        {
            return false;
        }

        return minWordSimilarity <= 0.0 || WordSimilarities(transcript, phrase).All(s => s >= minWordSimilarity);
    }

    /// <summary>
    /// Similarity of each expected word to the transcript letters aligned with it. The letters of the transcript and of the
    /// phrase are aligned by Levenshtein distance (backtrace preference: match, substitute, delete a phrase letter, insert a
    /// transcript letter). An edit belongs to the expected word holding the phrase letter; an inserted transcript letter
    /// belongs to the word of the preceding phrase letter (the first word if none). A word scores
    /// <c>1 - cost / max(word letters, transcript letters aligned to it)</c>, so a split or merged word is tolerated
    /// because the alignment runs on letters, and word order is enforced by the alignment (docs/design/evaluation-protocol.md, A2.4).
    /// </summary>
    public static double[] WordSimilarities(string transcript, string phrase)
    {
        var words = Normalize(Tags().Replace(phrase, " ")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var a = Letters(phrase);
        var b = Letters(transcript);
        var result = new double[words.Length];
        if (words.Length == 0 || b.Length == 0)
        {
            return result;
        }

        var wordOf = new int[a.Length];
        var at = 0;
        for (var k = 0; k < words.Length; k++)
        {
            for (var c = 0; c < words[k].Length; c++)
            {
                wordOf[at++] = k;
            }
        }

        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }

        var cost = new int[words.Length];
        var aligned = new int[words.Length];
        int row = a.Length, col = b.Length;
        while (row > 0 || col > 0)
        {
            if (row > 0 && col > 0 && a[row - 1] == b[col - 1] && d[row, col] == d[row - 1, col - 1])
            {
                aligned[wordOf[row - 1]]++;
                row--;
                col--;
            }
            else if (row > 0 && col > 0 && d[row, col] == d[row - 1, col - 1] + 1)
            {
                cost[wordOf[row - 1]]++;
                aligned[wordOf[row - 1]]++;
                row--;
                col--;
            }
            else if (row > 0 && d[row, col] == d[row - 1, col] + 1)
            {
                cost[wordOf[row - 1]]++;
                row--;
            }
            else
            {
                var owner = row > 0 ? wordOf[row - 1] : 0;
                cost[owner]++;
                aligned[owner]++;
                col--;
            }
        }

        for (var k = 0; k < words.Length; k++)
        {
            result[k] = 1.0 - (double)cost[k] / Math.Max(words[k].Length, aligned[k]);
        }

        return result;
    }

    private static string Letters(string text)
    {
        var withoutTags = Tags().Replace(text, " ");
        return Normalize(withoutTags).Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial System.Text.RegularExpressions.Regex Tags();
}
