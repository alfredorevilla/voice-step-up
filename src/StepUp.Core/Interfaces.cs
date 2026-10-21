namespace StepUp.Core;

public interface IVerifier
{
    /// <summary>Identifies the model and feature config. A stored voiceprint is valid only for the same id.</summary>
    string ModelId { get; }

    /// <summary>Returns a quality reject, or the probe embedding and transcript. Never scores or decides.</summary>
    Task<VerifierResult> AnalyzeAsync(ReadOnlyMemory<byte> wav, string expectedPhrase, CancellationToken cancellationToken);
}

public sealed record ChallengeIssueResult(Challenge? Challenge, ReasonCode? Reason);

public sealed record ChallengeCheckResult(string? Phrase, ReasonCode? Reason);

public interface IChallengeService
{
    Task<ChallengeIssueResult> IssueAsync(UserKey user, string action, CancellationToken cancellationToken);

    /// <summary>Checks the challenge is open and bound to this user and action, without consuming it.</summary>
    Task<ChallengeCheckResult> ValidateAsync(UserKey user, Guid challengeId, string action, CancellationToken cancellationToken);

    /// <summary>Atomically consumes the challenge. Only one concurrent caller succeeds.</summary>
    Task<ChallengeCheckResult> ConsumeAsync(UserKey user, Guid challengeId, string action, CancellationToken cancellationToken);
}

public interface IChallengeStore
{
    Task<int> CountOpenAsync(UserKey user, DateTimeOffset now, CancellationToken cancellationToken);

    Task AddAsync(Challenge challenge, CancellationToken cancellationToken);

    Task<Challenge?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Marks the challenge consumed and nulls its phrase if it is open and bound to this user and action. Returns the phrase, or null.</summary>
    Task<string?> TryConsumeAsync(Guid id, UserKey user, string action, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record FallbackResult(bool Fresh, string Why);

public interface IFallbackFactor
{
    FallbackResult Evaluate(Caller caller);
}

public interface IStepUpPolicy
{
    /// <summary>True when the action may proceed: a valid assertion, or fresh MFA through the fallback factor.</summary>
    bool IsSatisfied(Caller caller, bool assertionValid);
}

public interface IVoiceprintStore
{
    Task<Voiceprint?> GetAsync(UserKey user, CancellationToken cancellationToken);

    /// <summary>Atomically replaces any existing voiceprint.</summary>
    Task ReplaceAsync(Voiceprint voiceprint, CancellationToken cancellationToken);

    /// <summary>Deletes the row. Returns false if none existed.</summary>
    Task<bool> DeleteAsync(UserKey user, CancellationToken cancellationToken);
}

public interface IConsentStore
{
    Task<bool> IsActiveAsync(UserKey user, CancellationToken cancellationToken);

    /// <summary>Returns false if consent is already active.</summary>
    Task<bool> GiveAsync(UserKey user, string textVersion, CancellationToken cancellationToken);

    Task WithdrawAsync(UserKey user, CancellationToken cancellationToken);
}

public interface IAttemptLog
{
    Task AppendAsync(Attempt attempt, CancellationToken cancellationToken);

    Task<IReadOnlyList<Attempt>> ListAsync(AttemptFilter filter, CancellationToken cancellationToken);

    /// <summary>Consecutive failed voice attempts since the last pass or since <paramref name="notBefore"/>.</summary>
    Task<VoiceFailureState> GetFailureStateAsync(UserKey user, DateTimeOffset notBefore, CancellationToken cancellationToken);
}

public interface IThresholdStore
{
    Task<ThresholdEntry?> GetActiveAsync(string name, string modelId, CancellationToken cancellationToken);

    Task SetAsync(ThresholdEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<ThresholdEntry>> ListAsync(CancellationToken cancellationToken);
}

public interface IEnrollmentStore
{
    Task SaveAsync(EnrollmentState state, CancellationToken cancellationToken);

    Task<EnrollmentState?> GetAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Deletes every in-progress enrollment of this user (their sample embeddings included).</summary>
    Task DeleteForUserAsync(UserKey user, CancellationToken cancellationToken);

    /// <summary>Deletes enrollments whose time is up. Abandoned enrollments must not keep sample embeddings.</summary>
    Task PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IUsedAssertionStore
{
    /// <summary>Records the jti. Returns false if it was already used.</summary>
    Task<bool> TryUseAsync(string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

public interface IUserKeyHasher
{
    UserKey Hash(string tid, string oid);
}
