using StepUp.Eval;

namespace StepUp.Eval.Tests;

public class FreezeTests
{
    [Fact]
    public void Cohort_normalisation_is_adopted_when_p0_eer_drops_by_a_fifth_and_no_condition_worsens()
    {
        // raw P0 0.10 -> 0.08 is exactly 20% lower; the others are equal or lower
        var raw = new Dictionary<string, double> { ["P0"] = 0.10, ["P1"] = 0.12, ["P2"] = 0.20, ["P3"] = 0.30, ["P4"] = 0.15 };
        var norm = new Dictionary<string, double> { ["P0"] = 0.08, ["P1"] = 0.12, ["P2"] = 0.19, ["P3"] = 0.30, ["P4"] = 0.14 };
        Assert.True(Freeze.CohortAdopted(raw, norm));
    }

    [Fact]
    public void Cohort_normalisation_is_not_adopted_when_the_p0_gain_is_under_a_fifth()
    {
        var raw = new Dictionary<string, double> { ["P0"] = 0.10, ["P1"] = 0.12, ["P2"] = 0.20, ["P3"] = 0.30, ["P4"] = 0.15 };
        var norm = new Dictionary<string, double> { ["P0"] = 0.09, ["P1"] = 0.10, ["P2"] = 0.15, ["P3"] = 0.20, ["P4"] = 0.10 };
        Assert.False(Freeze.CohortAdopted(raw, norm));
    }

    [Fact]
    public void Cohort_normalisation_is_not_adopted_when_one_degraded_condition_gets_worse()
    {
        var raw = new Dictionary<string, double> { ["P0"] = 0.10, ["P1"] = 0.12, ["P2"] = 0.20, ["P3"] = 0.30, ["P4"] = 0.15 };
        var norm = new Dictionary<string, double> { ["P0"] = 0.05, ["P1"] = 0.12, ["P2"] = 0.21, ["P3"] = 0.20, ["P4"] = 0.10 };
        Assert.False(Freeze.CohortAdopted(raw, norm));
    }

    [Fact]
    public void Default_threshold_is_the_far_one_percent_point_rounded_up()
    {
        // 200 impostor scores 0.001 ... 0.200: 1% allows 2 false accepts (0.200 and 0.199), so t sits just above 0.198
        var impostor = Enumerable.Range(1, 200).Select(n => n / 1000.0).ToArray();
        // just above 0.198 rounds up to 0.20, which lets only 0.200 through: FAR 0.5%, never above the 1% target
        Assert.Equal(0.20, Freeze.DefaultThreshold(impostor, 0.01), 9);
    }

    [Fact]
    public void Consistency_floor_is_the_first_percentile_rounded_down()
    {
        // 101 values 0.500 ... 0.600: rank 0.01 * 100 = 1 -> the second value, 0.501 -> rounded down to 0.50
        var values = Enumerable.Range(0, 101).Select(n => 0.5 + (n / 1000.0)).ToArray();
        Assert.Equal(0.50, Freeze.ConsistencyFloor(values), 9);
    }

    [Fact]
    public void A_condition_with_no_scores_does_not_block_cohort_normalisation()
    {
        // P3 is refused entirely by the quality gate, so its EER is NaN on both sides: it is left out of the check
        var raw = new Dictionary<string, double> { ["P0"] = 0.10, ["P1"] = 0.12, ["P2"] = 0.20, ["P3"] = double.NaN, ["P4"] = 0.15 };
        var norm = new Dictionary<string, double> { ["P0"] = 0.05, ["P1"] = 0.10, ["P2"] = 0.15, ["P3"] = double.NaN, ["P4"] = 0.10 };
        Assert.True(Freeze.CohortAdopted(raw, norm));
    }

    [Fact]
    public void Cohort_normalisation_needs_a_p0_result()
    {
        var raw = new Dictionary<string, double> { ["P0"] = double.NaN, ["P1"] = 0.12, ["P2"] = 0.20, ["P3"] = 0.3, ["P4"] = 0.15 };
        var norm = new Dictionary<string, double> { ["P0"] = double.NaN, ["P1"] = 0.10, ["P2"] = 0.15, ["P3"] = 0.2, ["P4"] = 0.10 };
        Assert.False(Freeze.CohortAdopted(raw, norm));
    }
}
