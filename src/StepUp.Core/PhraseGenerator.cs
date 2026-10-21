using System.Security.Cryptography;

namespace StepUp.Core;

public static class PhraseGenerator
{
    public const int WordsPerPhrase = 4;

    /// <summary>Four words drawn with a CSPRNG, lowercase, space separated.</summary>
    public static string Create()
    {
        var chosen = new string[WordsPerPhrase];
        for (var i = 0; i < chosen.Length; i++)
        {
            chosen[i] = Wordlist.Words[RandomNumberGenerator.GetInt32(Wordlist.Words.Length)];
        }

        return string.Join(' ', chosen);
    }

    /// <summary>Lowercase, letters and digits only, single spaces. Used to compare a transcript with a phrase.</summary>
    public static string Normalize(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', new string([.. chars]).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool Matches(string? transcript, string phrase) =>
        transcript is not null && Normalize(transcript) == Normalize(phrase);
}
