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

    public static bool Matches(string? transcript, string phrase) =>
        !string.IsNullOrWhiteSpace(transcript) && Similarity(transcript, phrase) >= MatchThreshold;

    private static string Letters(string text)
    {
        var withoutTags = Tags().Replace(text, " ");
        return Normalize(withoutTags).Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial System.Text.RegularExpressions.Regex Tags();
}
