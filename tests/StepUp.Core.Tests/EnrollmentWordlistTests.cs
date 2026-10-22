namespace StepUp.Core.Tests;

public class EnrollmentWordlistTests
{
    [Fact]
    public void EnrollmentAndChallengeWordlists_ShareNoWords()
    {
        var shared = Wordlist.Words.Intersect(EnrollmentWordlist.Words, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Empty(shared);
        Assert.Equal(EnrollmentWordlist.Words.Length, EnrollmentWordlist.Words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EnrollmentPhrases_AreEnrollmentWordsAndNeverChallengeWords()
    {
        for (var i = 0; i < 200; i++)
        {
            var words = PhraseGenerator.CreateEnrollment().Split(' ');

            Assert.Equal(PhraseGenerator.WordsPerPhrase, words.Length);
            Assert.All(words, w => Assert.Contains(w, EnrollmentWordlist.Words));
            Assert.All(words, w => Assert.DoesNotContain(w, Wordlist.Words));
        }
    }
}
