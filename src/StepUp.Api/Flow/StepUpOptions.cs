using StepUp.Core;

namespace StepUp.Api.Flow;

public sealed class StepUpOptions
{
    public const string ThresholdName = "stepup";

    /// <summary>Actions a challenge can be issued for.</summary>
    public string[] Actions { get; set; } = ["payout"];

    public TimeSpan VerifierTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public int VerifierConcurrency { get; set; } = 4;

    public int EnrollmentSamples { get; set; } = 3;

    public TimeSpan EnrollmentTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Lowest pairwise cosine between enrollment samples. The 1st percentile of genuine enrollments on dev-clean (docs/eval-report.md, rule R3).</summary>
    public double EnrollmentConsistencyFloor { get; set; } = 0.46;

    /// <summary>Lowest threshold an analyst may set.</summary>
    public double ThresholdFloor { get; set; } = 0.3;

    /// <summary>The FAR 1% point of the 2 s probe on dev-clean, rounded up (docs/eval-report.md, rule R1). Tied to the speaker model id and to a probe of about 2 s.</summary>
    public double ThresholdSeed { get; set; } = 0.79;

    /// <summary>
    /// Every expected word of the challenge must reach this letter similarity (0 = off, the whole-phrase rule alone).
    /// See docs/design/evaluation-protocol.md A2.4 and docs/eval-report.md for how the value was chosen.
    /// </summary>
    public double PhraseMinWordSimilarity { get; set; }

    public int ChallengePerMinute { get; set; } = 20;

    public int VerifyPerMinute { get; set; } = 20;

    public int SamplePerMinute { get; set; } = 30;

    /// <summary>General per-user (or per-address, when anonymous) ceiling across all endpoints.</summary>
    public int GeneralPerMinute { get; set; } = 600;

    public int MaxPayeeLength { get; set; } = 200;

    public LockoutOptions Lockout { get; set; } = new();

    public ChallengeOptions Challenge { get; set; } = new();

    public FallbackOptions Fallback { get; set; } = new();

    public AssertionOptions Assertion { get; set; } = new();
}
