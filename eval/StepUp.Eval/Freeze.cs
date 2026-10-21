namespace StepUp.Eval;

/// <summary>The operating values chosen on dev-clean (protocol section 6) and then frozen.</summary>
public sealed record Frozen(
    double Threshold,
    double StrictThreshold,
    double ConsistencyFloor,
    double PhraseThreshold,
    bool CohortAdopted,
    IReadOnlyDictionary<string, double> RawEer,
    IReadOnlyDictionary<string, double> CohortEer)
{
    /// <summary>Amendment 2 (A2.2): the R1 threshold of each of L3, L3E, L4, L4E on dev-clean.</summary>
    public IReadOnlyDictionary<string, double> ConditionThresholds { get; init; } = new Dictionary<string, double>();

    /// <summary>Amendment 2 (A2.2): the R2 strict threshold of each of those conditions.</summary>
    public IReadOnlyDictionary<string, double> ConditionStrictThresholds { get; init; } = new Dictionary<string, double>();

    /// <summary>Amendment 2 (A2.2): the R3 floor for the 3.5 s enrollment (L3E, L4E).</summary>
    public double ExtendedConsistencyFloor { get; init; } = double.NaN;

    /// <summary>Amendment 2 (A2.4): the smallest per-word floor that met the gate on dev-clean, NaN if none did.</summary>
    public double PhraseWordFloor { get; init; } = double.NaN;

    /// <summary>Amendment 2 (A2.3): FRR on dev-clean at each condition's own R1 threshold, for P0, L3E and L4E.</summary>
    public IReadOnlyDictionary<string, double> FrrAtOwnThreshold { get; init; } = new Dictionary<string, double>();

    /// <summary>Amendment 2 (A2.3): R6, a 6-word challenge is adopted.</summary>
    public bool AdoptLongChallenge { get; init; }

    public double ThresholdFor(string condition) => ConditionThresholds.TryGetValue(condition, out var t) ? t : Threshold;

    public double StrictThresholdFor(string condition) => ConditionStrictThresholds.TryGetValue(condition, out var t) ? t : StrictThreshold;
}

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

    public const double LongChallengeMinFrrGain = 0.10;

    /// <summary>A2.3 (R6): FRR of the candidate (L3E) at least 10 percentage points below FRR of P0, both at their own R1 thresholds on dev-clean.</summary>
    public static bool AdoptLongChallenge(double frrP0, double frrCandidate) =>
        !double.IsNaN(frrP0) && !double.IsNaN(frrCandidate) && frrP0 - frrCandidate >= LongChallengeMinFrrGain - 1e-12;

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
