namespace StepUp.Core;

public sealed record ChallengeOptions
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxOpenPerUser { get; init; } = 3;
}

public sealed class ChallengeService(IChallengeStore store, ChallengeOptions options, TimeProvider time) : IChallengeService
{
    public async Task<ChallengeIssueResult> IssueAsync(UserKey user, string action, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (await store.CountOpenAsync(user, now, cancellationToken) >= options.MaxOpenPerUser)
        {
            return new ChallengeIssueResult(null, ReasonCode.RateLimited);
        }

        var challenge = new Challenge(Guid.NewGuid(), user, action, PhraseGenerator.Create(), now + options.Ttl, null);
        await store.AddAsync(challenge, cancellationToken);
        return new ChallengeIssueResult(challenge, null);
    }

    public async Task<ChallengeCheckResult> ValidateAsync(UserKey user, Guid challengeId, string action, CancellationToken cancellationToken)
    {
        var challenge = await store.GetAsync(challengeId, cancellationToken);
        return Check(challenge, user, action);
    }

    public async Task<ChallengeCheckResult> ConsumeAsync(UserKey user, Guid challengeId, string action, CancellationToken cancellationToken)
    {
        var challenge = await store.GetAsync(challengeId, cancellationToken);
        var check = Check(challenge, user, action);
        if (check.Reason is not null)
        {
            return check;
        }

        var phrase = await store.TryConsumeAsync(challengeId, user, action, time.GetUtcNow(), cancellationToken);
        return phrase is null
            ? new ChallengeCheckResult(null, ReasonCode.ChallengeConsumed)
            : new ChallengeCheckResult(phrase, null);
    }

    // A challenge of another user or action looks the same as an unknown one: no oracle for guessing ids.
    private ChallengeCheckResult Check(Challenge? challenge, UserKey user, string action)
    {
        if (challenge is null || challenge.User != user || !string.Equals(challenge.Action, action, StringComparison.Ordinal))
        {
            return new ChallengeCheckResult(null, ReasonCode.ChallengeExpired);
        }

        if (challenge.ConsumedAt is not null)
        {
            return new ChallengeCheckResult(null, ReasonCode.ChallengeConsumed);
        }

        return challenge.ExpiresAt <= time.GetUtcNow() || challenge.Phrase is null
            ? new ChallengeCheckResult(null, ReasonCode.ChallengeExpired)
            : new ChallengeCheckResult(challenge.Phrase, null);
    }
}
