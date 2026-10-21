using StepUp.Eval;

namespace StepUp.Eval.Tests;

// Expected values are worked out by hand in the comments.
public class MetricsTests
{
    [Fact]
    public void Far_counts_scores_at_or_above_the_threshold()
    {
        double[] impostor = [0.1, 0.2, 0.3, 0.4];
        // >= 0.3: 0.3 and 0.4 -> 2 of 4
        Assert.Equal(0.5, Metrics.Far(impostor, 0.3));
    }

    [Fact]
    public void Frr_counts_scores_below_the_threshold()
    {
        double[] genuine = [0.6, 0.7, 0.8, 0.9];
        // < 0.75: 0.6 and 0.7 -> 2 of 4
        Assert.Equal(0.5, Metrics.Frr(genuine, 0.75));
    }

    [Fact]
    public void Threshold_at_far_is_the_smallest_threshold_that_meets_the_target()
    {
        double[] impostor = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];
        // target 0.2 allows 2 of 10 false accepts: 0.9 and 1.0, so the threshold sits just above 0.8
        var t = Metrics.ThresholdAtFar(impostor, 0.2);
        Assert.Equal(0.2, Metrics.Far(impostor, t));
        Assert.Equal(0.3, Metrics.Far(impostor, Math.BitDecrement(t)));
    }

    [Fact]
    public void Threshold_at_far_zero_is_above_every_impostor_score()
    {
        double[] impostor = [0.1, 0.5, 0.9];
        var t = Metrics.ThresholdAtFar(impostor, 0.0);
        Assert.Equal(0.0, Metrics.Far(impostor, t));
        Assert.True(t > 0.9);
    }

    [Fact]
    public void Threshold_at_far_handles_ties_at_the_boundary()
    {
        double[] impostor = [0.5, 0.5, 0.5, 0.5];
        // 25% allows 1 false accept, but all four are equal: the threshold must go above 0.5
        var t = Metrics.ThresholdAtFar(impostor, 0.25);
        Assert.Equal(0.0, Metrics.Far(impostor, t));
    }

    [Fact]
    public void Eer_is_where_the_two_error_rates_meet()
    {
        double[] genuine = [0.6, 0.7, 0.8, 0.9];
        double[] impostor = [0.1, 0.2, 0.3, 0.65];
        // at t = 0.65: FRR = 1/4 (0.6), FAR = 1/4 (0.65) -> EER 0.25
        var (eer, threshold) = Metrics.Eer(genuine, impostor);
        Assert.Equal(0.25, eer, 9);
        Assert.Equal(0.65, threshold, 9);
    }

    [Fact]
    public void Eer_of_perfectly_separated_scores_is_zero()
    {
        var (eer, _) = Metrics.Eer([0.8, 0.9], [0.1, 0.2]);
        Assert.Equal(0.0, eer, 9);
    }

    [Fact]
    public void Wilson_with_no_errors_has_the_closed_form_upper_bound()
    {
        // k = 0, n = 10: lower 0, upper z^2 / (n + z^2) = 3.8416 / 13.8416 = 0.27754
        var (lo, hi) = Metrics.Wilson(0, 10);
        Assert.Equal(0.0, lo, 9);
        Assert.Equal(0.27754, hi, 4);
    }

    [Fact]
    public void Wilson_for_half_of_ten()
    {
        // centre (0.5 + 0.19208) = 0.69208; half-width 1.96 * sqrt(0.025 + 0.009604) = 0.36460; divide by 1.38416
        var (lo, hi) = Metrics.Wilson(5, 10);
        Assert.Equal(0.23659, lo, 4);
        Assert.Equal(0.76341, hi, 4);
    }

    [Theory]
    [InlineData(0.8049, 0.81)]
    [InlineData(0.81, 0.81)]
    [InlineData(0.8001, 0.81)]
    public void Round_up_keeps_two_decimals_and_never_lowers_the_value(double value, double expected) =>
        Assert.Equal(expected, Metrics.RoundUp2(value), 9);

    [Theory]
    [InlineData(0.5199, 0.51)]
    [InlineData(0.52, 0.52)]
    public void Round_down_keeps_two_decimals_and_never_raises_the_value(double value, double expected) =>
        Assert.Equal(expected, Metrics.RoundDown2(value), 9);

    [Fact]
    public void Percentile_interpolates_linearly_between_ranks()
    {
        // sorted 1,2,3,4: p = 0.5 -> rank 1.5 -> 2.5; p = 0.01 -> rank 0.03 -> 1.03
        double[] values = [4, 1, 3, 2];
        Assert.Equal(2.5, Metrics.Percentile(values, 0.5), 9);
        Assert.Equal(1.03, Metrics.Percentile(values, 0.01), 9);
    }

    [Fact]
    public void Cluster_bootstrap_is_reproducible_and_stays_inside_the_data()
    {
        var speakers = new List<SpeakerScores>
        {
            new(0, [0.9, 0.8], [0.1, 0.2]),
            new(1, [0.4, 0.3], [0.5, 0.1]),
            new(2, [0.95, 0.85], [0.15, 0.05]),
        };
        double Frr(IReadOnlyList<double> g, IReadOnlyList<double> i) => Metrics.Frr(g, 0.5);

        var first = Metrics.ClusterBootstrap(speakers, Frr, 500, 6001);
        var again = Metrics.ClusterBootstrap(speakers, Frr, 500, 6001);

        Assert.Equal(first, again);
        Assert.InRange(first.Lo, 0.0, 1.0);
        Assert.InRange(first.Hi, first.Lo, 1.0);
        // speaker 1 alone has FRR 1.0, the others 0.0; the 6-genuine pool gives 2/6 at most 1/3 per draw of speaker 1
        Assert.True(first.Hi >= 1.0 / 3.0 - 1e-9);
    }

    [Fact]
    public void Eer_of_a_condition_with_no_trials_is_not_a_number()
    {
        var (eer, threshold) = Metrics.Eer([], []);
        Assert.True(double.IsNaN(eer));
        Assert.True(double.IsNaN(threshold));
    }

    [Fact]
    public void Cluster_bootstrap_of_no_speakers_is_not_a_number()
    {
        var (lo, hi) = Metrics.ClusterBootstrap([], (g, i) => 0.0, 10, 1);
        Assert.True(double.IsNaN(lo));
        Assert.True(double.IsNaN(hi));
    }
}
