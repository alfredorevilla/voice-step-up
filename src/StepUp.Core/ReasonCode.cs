namespace StepUp.Core;

/// <summary>Reason codes from docs/architecture.md section 4. Detail codes stay in the attempt log.</summary>
public enum ReasonCode
{
    Muted,
    TooShort,
    Noisy,
    PhraseMismatch,
    BelowThreshold,
    Timeout,
    VerifierError,
    NoVoiceprint,
    ConsentMissing,
    ChallengeExpired,
    ChallengeConsumed,
    RateLimited,
    StepUpRequired,
    FreshMfaRequired,
    ReEnrollRequired,
    VerificationFailed,

    // Detail codes for the attempt log only. A client never sees these; it gets StepUpRequired.
    AssertionReplayed,
    AssertionInvalid,
}
