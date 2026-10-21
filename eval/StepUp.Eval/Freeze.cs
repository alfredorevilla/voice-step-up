namespace StepUp.Eval;

/// <summary>The operating values chosen on dev-clean (protocol section 6) and then frozen.</summary>
public sealed record Frozen(
    double Threshold,
    double StrictThreshold,
    double ConsistencyFloor,
    double PhraseThreshold,
    bool CohortAdopted,
    IReadOnlyDictionary<string, double> RawEer,
    IReadOnlyDictionary<string, double> CohortEer);

public static class Freeze
{
    public const double DefaultFar = 0.01;
    public const double StrictFar = 0.001;
    public const double ConsistencyPercentile = 0.01;
    public const double CohortMinRelativeGain = 0.20;

    private static readonly string[] DegradedConditions = ["P1", "P2", "P3", "P4"];

    /// <summary>R1/R2: the FAR point on the impostor scores, rounded up to two decimals (rounding up never raises FAR).</summary>
    public static double DefaultThreshold(IReadOnlyList<double> impostor, double far) =>
        Metrics.RoundUp2(Metrics.ThresholdAtFar(impostor, far));

    /// <summary>R3: the first percentile of the genuine enrollments' lowest pairwise cosine, rounded down.</summary>
    public static double ConsistencyFloor(IReadOnlyList<double> minimumPairwise) =>
        Metrics.RoundDown2(Metrics.Percentile(minimumPairwise, ConsistencyPercentile));

    /// <summary>R5: P0 EER at least 20% lower and no EER higher in P1 to P4.</summary>
    public static bool CohortAdopted(IReadOnlyDictionary<string, double> rawEer, IReadOnlyDictionary<string, double> cohortEer)
    {
        if (double.IsNaN(rawEer["P0"]) || double.IsNaN(cohortEer["P0"]) || cohortEer["P0"] > rawEer["P0"] * (1.0 - CohortMinRelativeGain) + 1e-12)
        {
            return false;
        }

        // A condition without scores (for example every probe refused by the quality gate) cannot get worse.
        return DegradedConditions.All(c => double.IsNaN(rawEer[c]) || double.IsNaN(cohortEer[c]) || cohortEer[c] <= rawEer[c] + 1e-12);
    }
}
