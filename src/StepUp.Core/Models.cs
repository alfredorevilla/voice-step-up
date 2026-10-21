namespace StepUp.Core;

/// <summary>Pseudonymous user key: HMAC of tenant and object id, never the raw ids.</summary>
public readonly record struct UserKey(string Value)
{
    public override string ToString() => Value;
}

/// <summary>The authenticated caller as the service sees it, after token validation.</summary>
public sealed record Caller(string Tid, string Oid, IReadOnlyList<string> Amr, DateTimeOffset? AuthTime);

public enum AttemptMode { Voice, Fallback }

/// <summary>Pass and Fail count toward lockout (Fail only). Rejected is a quality reject, Error a verifier fault.</summary>
public enum AttemptResult { Pass, Fail, Rejected, Error }

public sealed record Attempt(
    Guid Id,
    UserKey User,
    DateTimeOffset At,
    string Action,
    double? Score,
    double Threshold,
    string ModelId,
    AttemptResult Result,
    ReasonCode? Reason,
    AttemptMode Mode);

public sealed record Challenge(Guid Id, UserKey User, string Action, string? Phrase, DateTimeOffset ExpiresAt, DateTimeOffset? ConsumedAt);

public sealed record Voiceprint(UserKey User, string ModelId, float[] Embedding, DateTimeOffset CreatedAt);

public sealed record ThresholdEntry(string Name, string ModelId, double Value, DateTimeOffset UpdatedAt, string UpdatedBy);

public sealed record EnrollmentState(Guid Id, UserKey User, IReadOnlyList<string> Phrases, IReadOnlyList<float[]> Samples, DateTimeOffset ExpiresAt);

/// <summary>Output of a verifier: a quality reject, or the probe embedding and the transcript.</summary>
public sealed record VerifierResult(ReasonCode? Reject, float[]? Embedding, string? Transcript);

public sealed record VoiceFailureState(int ConsecutiveFailures, DateTimeOffset? LastFailureAt);

public sealed record AttemptFilter(DateTimeOffset? From, DateTimeOffset? To, AttemptResult? Result, int Skip, int Take);
