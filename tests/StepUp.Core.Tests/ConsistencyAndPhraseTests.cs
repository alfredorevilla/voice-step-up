using StepUp.Core;

namespace StepUp.Core.Tests;

public class ConsistencyAndPhraseTests
{
    // [1,0] and [3,4] have cosine 3/5 = 0.6 exactly in double arithmetic.
    private static readonly float[] A = [1f, 0f];
    private static readonly float[] B = [3f, 4f];

    [Fact]
    public void Consistency_pair_exactly_at_the_floor_passes_and_just_above_it_fails()
    {
        Assert.True(Cosine.AllPairsAtLeast([A, B], 0.6));
        Assert.False(Cosine.AllPairsAtLeast([A, B], 0.6000001));
        Assert.True(Cosine.AllPairsAtLeast([A, B], 0.5999999));
    }

    [Fact]
    public void Consistency_looks_at_the_worst_pair_not_the_average()
    {
        // A and A' are identical (1.0); only the pair with B is low
        Assert.False(Cosine.AllPairsAtLeast([A, [1f, 0f], B], 0.61));
        Assert.Equal(0.6, Cosine.MinimumPairwise([A, [1f, 0f], B]), 9);
    }

    [Fact]
    public void Consistency_of_fewer_than_two_samples_never_fails_the_floor()
    {
        Assert.True(Cosine.AllPairsAtLeast([A], 0.99));
        Assert.True(Cosine.AllPairsAtLeast([], 0.99));
    }

    // Documented limit (docs/adr/0002, threat model TM-001): the letter-similarity rule accepts a recording whose
    // phrase differs from the challenge by one word. M6 measured this at 87% of such phrases on test-clean.
    [Fact]
    public void PhraseCheck_OneWrongWord_IsAcceptedAsADocumentedLimit()
    {
        const string challenge = "apple bridge candle river";

        Assert.True(PhraseGenerator.Matches("apple bridge candle table", challenge));
        Assert.True(PhraseGenerator.Matches("apple bridge candle river", challenge));
    }

    [Fact]
    public void PhraseCheck_ADifferentWholePhrase_IsRejected()
    {
        Assert.False(PhraseGenerator.Matches("orange violin ladder meadow", "apple bridge candle river"));
    }
}
