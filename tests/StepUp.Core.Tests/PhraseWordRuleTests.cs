using StepUp.Core;

namespace StepUp.Core.Tests;

public class PhraseWordRuleTests
{
    private const string Phrase = "apple bridge candle river";

    [Fact]
    public void An_exact_transcript_scores_one_for_every_word()
    {
        var similarities = PhraseGenerator.WordSimilarities("Apple bridge, candle river.", Phrase);

        Assert.Equal(4, similarities.Length);
        Assert.All(similarities, s => Assert.Equal(1.0, s, 9));
    }

    [Fact]
    public void One_wrong_word_drags_down_only_that_word()
    {
        // "table" shares no useful alignment with "river": five edits over five letters
        var similarities = PhraseGenerator.WordSimilarities("apple bridge candle table", Phrase);

        Assert.Equal(1.0, similarities[0], 9);
        Assert.Equal(1.0, similarities[1], 9);
        Assert.Equal(1.0, similarities[2], 9);
        Assert.Equal(0.0, similarities[3], 9);
    }

    [Fact]
    public void The_whole_phrase_rule_accepts_the_near_miss_and_the_word_rule_rejects_it()
    {
        Assert.True(PhraseGenerator.Matches("apple bridge candle table", Phrase));
        Assert.False(PhraseGenerator.Matches("apple bridge candle table", Phrase, minWordSimilarity: 0.5));
    }

    [Fact]
    public void A_split_and_merged_word_is_scored_per_word_by_the_alignment()
    {
        // Whisper heard "cliff peach" as "Lift each": "cliff"->"lift" takes two edits (delete c, f->t) = 0.6,
        // "peach"->"each" one edit (delete p) = 0.8, and the other two words are exact.
        var similarities = PhraseGenerator.WordSimilarities("Lift each marble drum", "cliff peach marble drum");

        Assert.Equal(0.6, similarities[0], 9);
        Assert.Equal(0.8, similarities[1], 9);
        Assert.Equal(1.0, similarities[2], 9);
        Assert.Equal(1.0, similarities[3], 9);
        Assert.True(PhraseGenerator.Matches("Lift each marble drum", "cliff peach marble drum", minWordSimilarity: 0.6));
        Assert.False(PhraseGenerator.Matches("Lift each marble drum", "cliff peach marble drum", minWordSimilarity: 0.65));
    }

    [Fact]
    public void Words_in_the_wrong_order_are_rejected_by_the_word_rule()
    {
        Assert.False(PhraseGenerator.Matches("river candle bridge apple", Phrase, minWordSimilarity: 0.6));
    }

    [Fact]
    public void An_extra_trailing_word_is_charged_to_the_last_expected_word()
    {
        var similarities = PhraseGenerator.WordSimilarities("apple bridge candle river extra", Phrase);

        Assert.Equal(1.0, similarities[0], 9);
        Assert.Equal(1.0, similarities[2], 9);
        Assert.Equal(0.5, similarities[3], 9);
    }

    [Fact]
    public void An_empty_or_blank_transcript_never_matches_and_scores_zero()
    {
        Assert.All(PhraseGenerator.WordSimilarities("", Phrase), s => Assert.Equal(0.0, s, 9));
        Assert.False(PhraseGenerator.Matches("", Phrase, minWordSimilarity: 0.5));
        Assert.False(PhraseGenerator.Matches(null, Phrase, minWordSimilarity: 0.0));
    }

    [Fact]
    public void A_floor_of_zero_is_exactly_the_whole_phrase_rule()
    {
        foreach (var transcript in new[] { "apple bridge candle table", "orange violin ladder meadow", "apple bridge", Phrase })
        {
            Assert.Equal(PhraseGenerator.Matches(transcript, Phrase), PhraseGenerator.Matches(transcript, Phrase, minWordSimilarity: 0.0));
        }
    }
}
