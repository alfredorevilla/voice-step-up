using StepUp.Core;

namespace StepUp.Api.Flow;

/// <summary>Outcome of a verify call. <see cref="Code"/> is what the client may see; detail stays in the attempt log.</summary>
public sealed record VerifyOutcome(int Status, ReasonCode? Code, bool OfferFallback, string? Assertion, DateTimeOffset? ExpiresAt, TimeSpan? RetryAfter);

/// <summary>Limits how many verifications run at once (the verifier is CPU bound and in-process).</summary>
public sealed class VerifierGate(StepUpOptions options)
{
    public SemaphoreSlim Semaphore { get; } = new(options.VerifierConcurrency);

    private readonly HashSet<string> _busyUsers = [];

    /// <summary>One verification per user at a time, so the lockout check and the attempt write cannot interleave across requests.</summary>
    public bool TryEnterUser(string user)
    {
        lock (_busyUsers)
        {
            return _busyUsers.Add(user);
        }
    }

    public void ExitUser(string user)
    {
        lock (_busyUsers)
        {
            _busyUsers.Remove(user);
        }
    }
}

public sealed class VerificationService(
    IVerifier verifier,
    IChallengeService challenges,
    IVoiceprintStore voiceprints,
    IConsentStore consents,
    IAttemptLog attempts,
    IThresholdStore thresholds,
    StepUpAssertionService assertions,
    VerifierGate gate,
    StepUpOptions options,
    TimeProvider time)
{
    public async Task<VerifyOutcome> VerifyAsync(Caller caller, UserKey user, Guid challengeId, string action, string? payloadHash, ReadOnlyMemory<byte> wav, CancellationToken cancellationToken)
    {
        // A second verify for the same user while one is running is turned away like a full verifier gate (429, retry), not queued.
        if (!gate.TryEnterUser(user.Value))
        {
            return new VerifyOutcome(429, ReasonCode.RateLimited, true, null, null, TimeSpan.FromSeconds(1));
        }

        try
        {
            return await VerifyCoreAsync(caller, user, challengeId, action, payloadHash, wav, cancellationToken);
        }
        finally
        {
            gate.ExitUser(user.Value);
        }
    }

    private async Task<VerifyOutcome> VerifyCoreAsync(Caller caller, UserKey user, Guid challengeId, string action, string? payloadHash, ReadOnlyMemory<byte> wav, CancellationToken cancellationToken)
    {
        var voiceprint = await voiceprints.GetAsync(user, cancellationToken);
        if (voiceprint is null)
        {
            return await RefuseAndLogAsync(user, action, 403, ReasonCode.NoVoiceprint, cancellationToken: cancellationToken);
        }

        if (!await consents.IsActiveAsync(user, cancellationToken))
        {
            return await RefuseAndLogAsync(user, action, 403, ReasonCode.ConsentMissing, cancellationToken: cancellationToken);
        }

        if (voiceprint.ModelId != verifier.ModelId)
        {
            await LogAsync(user, action, null, 0, AttemptResult.Error, ReasonCode.ReEnrollRequired, cancellationToken);
            return Refuse(403, ReasonCode.ReEnrollRequired);
        }

        var failures = await attempts.GetFailureStateAsync(user, voiceprint.CreatedAt, cancellationToken);
        var lockout = VoiceLockout.Evaluate(failures, options.Lockout, time.GetUtcNow());
        if (lockout.Kind != LockoutKind.None)
        {
            return await RefuseAndLogAsync(user, action, 429, ReasonCode.RateLimited, retryAfter: lockout.RetryAfter, cancellationToken: cancellationToken);
        }

        var check = await challenges.ValidateAsync(user, challengeId, action, cancellationToken);
        if (check.Reason is { } invalid)
        {
            return await RefuseAndLogAsync(user, action, 410, invalid, fallback: false, cancellationToken: cancellationToken);
        }

        var threshold = await thresholds.GetActiveAsync(StepUpOptions.ThresholdName, verifier.ModelId, cancellationToken);
        if (threshold is null)
        {
            await LogAsync(user, action, null, 0, AttemptResult.Error, ReasonCode.VerifierError, cancellationToken);
            return Refuse(503, ReasonCode.VerifierError);
        }

        var analysis = await AnalyzeAsync(wav, check.Phrase!, cancellationToken);
        if (analysis.Outcome is { } failed)
        {
            await LogAsync(user, action, null, threshold.Value, AttemptResult.Error, failed.Code, cancellationToken);
            return failed;
        }

        if (analysis.Result!.Reject is { } quality)
        {
            // A quality reject does not consume the challenge and does not count toward the NIST failure counter.
            await LogAsync(user, action, null, threshold.Value, AttemptResult.Rejected, quality, cancellationToken);
            return new VerifyOutcome(400, quality, false, null, null, null);
        }

        var consumed = await challenges.ConsumeAsync(user, challengeId, action, cancellationToken);
        if (consumed.Reason is { } gone)
        {
            return await RefuseAndLogAsync(user, action, 410, gone, fallback: false, threshold: threshold.Value, cancellationToken: cancellationToken);
        }

        // Both checks always run, so the response and its timing do not say which one failed.
        double score;
        try
        {
            score = Cosine.Similarity(voiceprint.Embedding, analysis.Result.Embedding!);
        }
        catch (ArgumentException)
        {
            await LogAsync(user, action, null, threshold.Value, AttemptResult.Error, ReasonCode.ReEnrollRequired, cancellationToken);
            return Refuse(403, ReasonCode.ReEnrollRequired);
        }

        var phraseOk = PhraseGenerator.Matches(analysis.Result.Transcript, consumed.Phrase!, options.PhraseMinWordSimilarity);
        var scoreOk = score >= threshold.Value;
        if (!phraseOk || !scoreOk)
        {
            var detail = !phraseOk ? ReasonCode.PhraseMismatch : ReasonCode.BelowThreshold;
            await LogAsync(user, action, score, threshold.Value, AttemptResult.Fail, detail, cancellationToken);
            return new VerifyOutcome(422, ReasonCode.VerificationFailed, true, null, null, null);
        }

        await LogAsync(user, action, score, threshold.Value, AttemptResult.Pass, null, cancellationToken);
        var issued = assertions.Issue(new AssertionBinding(caller.Oid, caller.Tid, action, payloadHash));
        return new VerifyOutcome(200, null, false, issued.Token, issued.ExpiresAt, null);
    }

    private async Task<(VerifierResult? Result, VerifyOutcome? Outcome)> AnalyzeAsync(ReadOnlyMemory<byte> wav, string phrase, CancellationToken cancellationToken)
    {
        if (!await gate.Semaphore.WaitAsync(0, cancellationToken))
        {
            return (null, new VerifyOutcome(429, ReasonCode.RateLimited, true, null, null, TimeSpan.FromSeconds(1)));
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.VerifierTimeout);
            try
            {
                return (await verifier.AnalyzeAsync(wav, phrase, timeout.Token), null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (null, new VerifyOutcome(503, ReasonCode.Timeout, true, null, null, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any verifier fault fails closed: the fallback instruction, never a pass.
                return (null, new VerifyOutcome(503, ReasonCode.VerifierError, true, null, null, null));
            }
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    private Task LogAsync(UserKey user, string action, double? score, double threshold, AttemptResult result, ReasonCode? reason, CancellationToken cancellationToken) =>
        attempts.AppendAsync(new Attempt(Guid.NewGuid(), user, time.GetUtcNow(), action, score, threshold, verifier.ModelId, result, reason, AttemptMode.Voice), cancellationToken);

    /// <summary>A request turned away before any decision. It is logged as Refused, which never feeds the lockout counter.</summary>
    private async Task<VerifyOutcome> RefuseAndLogAsync(
        UserKey user, string action, int status, ReasonCode code, bool fallback = true, TimeSpan? retryAfter = null, double threshold = 0, CancellationToken cancellationToken = default)
    {
        await LogAsync(user, action, null, threshold, AttemptResult.Refused, code, cancellationToken);
        return new VerifyOutcome(status, code, fallback, null, null, retryAfter);
    }

    private static VerifyOutcome Refuse(int status, ReasonCode code, bool fallback = true) => new(status, code, fallback, null, null, null);
}
