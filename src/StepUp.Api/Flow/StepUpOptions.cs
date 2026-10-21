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

    /// <summary>Lowest pairwise cosine between enrollment samples. A placeholder until M6 sets it from data.</summary>
    public double EnrollmentConsistencyFloor { get; set; } = 0.5;

    /// <summary>Lowest threshold an analyst may set.</summary>
    public double ThresholdFloor { get; set; } = 0.3;

    public double ThresholdSeed { get; set; } = 0.8;

    public int ChallengePerMinute { get; set; } = 20;

    public int VerifyPerMinute { get; set; } = 20;

    public int SamplePerMinute { get; set; } = 30;

    public LockoutOptions Lockout { get; set; } = new();

    public ChallengeOptions Challenge { get; set; } = new();

    public FallbackOptions Fallback { get; set; } = new();

    public AssertionOptions Assertion { get; set; } = new();
}
