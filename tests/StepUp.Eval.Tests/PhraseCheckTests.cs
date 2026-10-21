using StepUp.Eval;

namespace StepUp.Eval.Tests;

public class PhraseCheckTests
{
    private static PhraseScores S(double correct, double wrong, double near = 0.0) => new("c", correct, wrong, near);

    [Fact]
    public void The_current_threshold_is_kept_when_it_meets_both_limits()
    {
        // 10 cases: all correct >= 0.6, one wrong at 0.5 (below 0.6): correct 100%, wrong 0%
        var scores = Enumerable.Range(0, 10).Select(_ => S(0.9, 0.3)).ToArray();
        Assert.Equal(0.60, PhraseCheck.ChooseThreshold(scores, 0.60), 9);
    }

    [Fact]
    public void A_higher_threshold_is_chosen_when_wrong_phrases_pass_at_the_current_one()
    {
        // 100 cases, correct always 0.95; wrong phrases score 0.7 in 5 cases, so 0.60 lets 5% through (> 1%)
        var scores = Enumerable.Range(0, 100).Select(n => S(0.95, n < 5 ? 0.7 : 0.2)).ToArray();
        // 0.60 and 0.65 and 0.70 still accept the 5 wrong cases (>=). From 0.75 up wrong is 0% and correct stays 100%
        // up to 0.95, so the tie among 0.75 ... 0.95 goes to the higher threshold, as the protocol says.
        Assert.Equal(0.95, PhraseCheck.ChooseThreshold(scores, 0.60), 9);
    }

    [Fact]
    public void A_lower_threshold_is_chosen_when_the_current_one_rejects_too_many_correct_phrases()
    {
        // 20 cases, 4 correct ones score 0.5 and 16 score 0.8; wrong phrases score 0.1
        // at 0.60: 16/20 = 80% correct (under the 90% limit). At 0.50 all 20 pass, and no higher value does as well.
        var weak = Enumerable.Range(0, 20).Select(n => S(n < 4 ? 0.5 : 0.8, 0.1)).ToArray();
        Assert.Equal(0.50, PhraseCheck.ChooseThreshold(weak, 0.60), 9);
    }

    [Fact]
    public void No_threshold_is_returned_when_no_grid_value_keeps_wrong_phrases_out()
    {
        var scores = Enumerable.Range(0, 10).Select(_ => S(0.9, 0.99)).ToArray();
        Assert.True(double.IsNaN(PhraseCheck.ChooseThreshold(scores, 0.60)));
    }

    [Fact]
    public void Rates_count_each_kind_at_the_threshold()
    {
        PhraseScores[] scores = [S(0.9, 0.2, 0.7), S(0.5, 0.65, 0.4), S(0.8, 0.1, 0.9), S(0.61, 0.0, 0.1)];
        var rates = PhraseCheck.Rates(scores, 0.60);
        Assert.Equal(3.0 / 4, rates.CorrectAccepted); // 0.9, 0.8, 0.61
        Assert.Equal(1.0 / 4, rates.WrongAccepted);   // 0.65
        Assert.Equal(2.0 / 4, rates.NearMissAccepted); // 0.7, 0.9
    }

    [Fact]
    public void A_near_miss_replaces_exactly_one_word_with_a_different_one()
    {
        var near = PhraseCheck.NearMiss("the cat sat down", "a dog ran off", new Random(5));
        var changed = near.Split(' ').Zip("the cat sat down".Split(' '), (a, b) => a != b).Count(x => x);
        Assert.Equal(1, changed);
        Assert.Equal(4, near.Split(' ').Length);
    }
}
