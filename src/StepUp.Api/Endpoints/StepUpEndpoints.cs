using Microsoft.AspNetCore.Authorization;
using StepUp.Api.Auth;
using StepUp.Api.Flow;
using StepUp.Core;

namespace StepUp.Api.Endpoints;

public static class StepUpEndpoints
{
    public static void MapStepUpApi(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        var api = app.MapGroup("/api").RequireAuthorization(AuthSetup.UserPolicy).AddEndpointFilter<StepUpFilter>();

        api.MapGet("/me", Me);
        api.MapPost("/enrollment/consent", GiveConsent);
        api.MapPost("/enrollment/start", StartEnrollment).RequireRateLimiting("challenge");
        api.MapPost("/enrollment/sample", AddSample).RequireRateLimiting("sample");
        api.MapPost("/enrollment/complete", CompleteEnrollment);
        api.MapDelete("/voiceprint", DeleteVoiceprint);
        api.MapPost("/stepup/challenge", IssueChallenge).RequireRateLimiting("challenge");
        api.MapPost("/stepup/verify", Verify).RequireRateLimiting("verify");
        api.MapPost("/payouts", Payout);

        var analyst = app.MapGroup("/api/analyst").RequireAuthorization(AuthSetup.AnalystPolicy);
        analyst.MapGet("/attempts", ListAttempts);
        analyst.MapGet("/thresholds", ListThresholds);
        analyst.MapPut("/thresholds", SetThreshold);
    }

    private static (Caller Caller, UserKey User) Who(HttpContext http)
    {
        var caller = CallerFactory.From(http.User)!;
        return (caller, http.RequestServices.GetRequiredService<IUserKeyHasher>().Hash(caller.Tid, caller.Oid));
    }

    private static async Task<IResult> Me(HttpContext http, IConsentStore consents, IVoiceprintStore voiceprints, CancellationToken ct)
    {
        var (caller, user) = Who(http);
        return Results.Ok(new
        {
            oid = caller.Oid,
            tid = caller.Tid,
            name = http.User.FindFirst("name")?.Value,
            enrolled = await voiceprints.GetAsync(user, ct) is not null,
            consent = await consents.IsActiveAsync(user, ct),
        });
    }

    private static async Task<IResult> GiveConsent(HttpContext http, ConsentRequest body, IConsentStore consents, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.TextVersion) || body.TextVersion.Length > 32)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "textVersion is required (at most 32 characters).");
        }

        return await consents.GiveAsync(Who(http).User, body.TextVersion, ct)
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Consent is already given.");
    }

    private static async Task<IResult> StartEnrollment(
        HttpContext http, IConsentStore consents, IFallbackFactor fallback, IEnrollmentStore enrollments, StepUpOptions options, TimeProvider time, CancellationToken ct)
    {
        var (caller, user) = Who(http);
        if (!fallback.Evaluate(caller).Fresh)
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.FreshMfaRequired, fallback: true);
        }

        if (!await consents.IsActiveAsync(user, ct))
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.ConsentMissing);
        }

        await enrollments.PurgeExpiredAsync(time.GetUtcNow(), ct);
        var phrases = Enumerable.Range(0, options.EnrollmentSamples).Select(_ => PhraseGenerator.CreateEnrollment()).ToList();
        var state = new EnrollmentState(Guid.NewGuid(), user, phrases, [], time.GetUtcNow() + options.EnrollmentTtl);
        await enrollments.SaveAsync(state, ct);
        return Results.Ok(new { enrollmentId = state.Id, phrases, samplesNeeded = options.EnrollmentSamples });
    }

    private static async Task<IResult> AddSample(
        HttpContext http, Guid enrollmentId, IConsentStore consents, IEnrollmentStore enrollments, IVerifier verifier, VerifierGate gate, StepUpOptions options, TimeProvider time, CancellationToken ct)
    {
        var user = Who(http).User;
        if (!await consents.IsActiveAsync(user, ct))
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.ConsentMissing);
        }

        var state = await enrollments.GetAsync(enrollmentId, time.GetUtcNow(), ct);

        // Another user's id looks like a forbidden resource; an unknown or expired one is 404.
        if (state is null)
        {
            return Results.NotFound();
        }

        if (state.User != user)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This enrollment belongs to someone else.");
        }

        if (state.Samples.Count >= state.Phrases.Count)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "All samples are already recorded; complete the enrollment.");
        }

        var (wav, error) = await AudioBody.ReadAsync(http.Request, ct);
        if (error is not null)
        {
            return error;
        }

        if (!await gate.Semaphore.WaitAsync(0, ct))
        {
            return Problems.Create(StatusCodes.Status429TooManyRequests, ReasonCode.RateLimited, retryAfter: TimeSpan.FromSeconds(1));
        }

        VerifierResult result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.VerifierTimeout);
            result = await verifier.AnalyzeAsync(wav!, state.Phrases[state.Samples.Count], timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Problems.Create(StatusCodes.Status503ServiceUnavailable, ReasonCode.Timeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problems.Create(StatusCodes.Status503ServiceUnavailable, ReasonCode.VerifierError);
        }
        finally
        {
            gate.Semaphore.Release();
        }

        if (result.Reject is { } quality)
        {
            return Problems.Create(StatusCodes.Status400BadRequest, quality);
        }

        if (!PhraseGenerator.Matches(result.Transcript, state.Phrases[state.Samples.Count], options.PhraseMinWordSimilarity))
        {
            return Problems.Create(StatusCodes.Status400BadRequest, ReasonCode.PhraseMismatch);
        }

        await enrollments.SaveAsync(state with { Samples = [.. state.Samples, result.Embedding!] }, ct);
        return Results.Ok(new { accepted = true, remaining = state.Phrases.Count - state.Samples.Count - 1 });
    }

    private static async Task<IResult> CompleteEnrollment(
        HttpContext http, Guid enrollmentId, IFallbackFactor fallback, IConsentStore consents, IEnrollmentStore enrollments, IVoiceprintStore voiceprints, IVerifier verifier, StepUpOptions options, TimeProvider time, CancellationToken ct)
    {
        var (caller, user) = Who(http);
        if (!fallback.Evaluate(caller).Fresh)
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.FreshMfaRequired, fallback: true);
        }

        // Consent withdrawn mid-enrollment: nothing may be stored (biometric-data rule: consent before enrollment).
        if (!await consents.IsActiveAsync(user, ct))
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.ConsentMissing);
        }

        var state = await enrollments.GetAsync(enrollmentId, time.GetUtcNow(), ct);
        if (state is null)
        {
            return Results.NotFound();
        }

        if (state.User != user)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This enrollment belongs to someone else.");
        }

        if (state.Samples.Count < options.EnrollmentSamples)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not enough samples.");
        }

        // The samples must come from one speaker, or a poisoned average could accept two people (TM-003).
        if (!Cosine.AllPairsAtLeast(state.Samples, options.EnrollmentConsistencyFloor))
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The samples do not sound like one speaker.", extensions: new Dictionary<string, object?> { ["code"] = nameof(ReasonCode.VerificationFailed) });
        }

        await voiceprints.ReplaceAsync(new Voiceprint(user, verifier.ModelId, Cosine.NormalizedMean(state.Samples), time.GetUtcNow()), ct);
        await enrollments.DeleteAsync(state.Id, ct);
        return Results.Ok(new { enrolled = true });
    }

    private static async Task<IResult> DeleteVoiceprint(HttpContext http, IUserDataEraser eraser, CancellationToken ct)
    {
        await eraser.EraseAsync(Who(http).User, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> IssueChallenge(
        HttpContext http, ChallengeRequest body, IChallengeService challenges, IConsentStore consents, IVoiceprintStore voiceprints, IVerifier verifier, StepUpOptions options, CancellationToken ct)
    {
        if (!options.Actions.Contains(body.Action, StringComparer.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown action.");
        }

        var user = Who(http).User;
        var voiceprint = await voiceprints.GetAsync(user, ct);
        if (voiceprint is null)
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.NoVoiceprint, fallback: true);
        }

        if (!await consents.IsActiveAsync(user, ct))
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.ConsentMissing, fallback: true);
        }

        if (voiceprint.ModelId != verifier.ModelId)
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.ReEnrollRequired, fallback: true);
        }

        var issued = await challenges.IssueAsync(user, body.Action, ct);
        return issued.Challenge is { } c
            ? Results.Ok(new { challengeId = c.Id, phrase = c.Phrase, expiresAt = c.ExpiresAt })
            : Problems.Create(StatusCodes.Status429TooManyRequests, issued.Reason!.Value);
    }

    private static async Task<IResult> Verify(HttpContext http, Guid challengeId, string action, string? amount, string? to, VerificationService verification, StepUpOptions options, CancellationToken ct)
    {
        // A payout assertion is bound to its amount and payee, so one voice pass cannot pay something else (TM-006).
        string? payloadHash = null;
        if (action == "payout")
        {
            if (!decimal.TryParse(amount, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || string.IsNullOrWhiteSpace(to) || to.Length > options.MaxPayeeLength)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A payout step-up needs amount and to.");
            }

            payloadHash = new PayoutRequest(parsed, to).PayloadHash;
        }

        var (caller, user) = Who(http);
        var (wav, error) = await AudioBody.ReadAsync(http.Request, ct);
        if (error is not null)
        {
            return error;
        }

        var outcome = await verification.VerifyAsync(caller, user, challengeId, action, payloadHash, wav!, ct);
        if (outcome.Status == StatusCodes.Status200OK)
        {
            return Results.Ok(new { assertion = outcome.Assertion, expiresAt = outcome.ExpiresAt });
        }

        return Problems.Create(outcome.Status, outcome.Code!.Value, outcome.OfferFallback, outcome.RetryAfter);
    }

    [RequiresStepUp("payout")]
    private static IResult Payout(PayoutRequest body, StepUpOptions options) =>
        body.Amount <= 0 || string.IsNullOrWhiteSpace(body.To) || body.To.Length > options.MaxPayeeLength
            ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "amount and to are required.")
            : Results.Ok(new { status = "accepted", amount = body.Amount, to = body.To });

    private static async Task<IResult> ListAttempts(DateTimeOffset? from, DateTimeOffset? to, AttemptResult? result, int? skip, int? take, IAttemptLog attempts, CancellationToken ct)
    {
        var rows = await attempts.ListAsync(new AttemptFilter(from, to, result, Math.Max(skip ?? 0, 0), Math.Clamp(take ?? 50, 1, 200)), ct);
        return Results.Ok(rows.Select(a => new
        {
            a.Id,
            user = a.User.Value[..8],
            a.At,
            a.Action,
            a.Score,
            a.Threshold,
            a.ModelId,
            result = a.Result.ToString(),
            reason = a.Reason?.ToString(),
            mode = a.Mode.ToString(),
        }));
    }

    private static async Task<IResult> ListThresholds(IThresholdStore thresholds, CancellationToken ct) =>
        Results.Ok(await thresholds.ListAsync(ct));

    private static async Task<IResult> SetThreshold(HttpContext http, ThresholdRequest body, IThresholdStore thresholds, IVerifier verifier, StepUpOptions options, TimeProvider time, CancellationToken ct)
    {
        if (body.Name != StepUpOptions.ThresholdName || double.IsNaN(body.Value) || body.Value < options.ThresholdFloor || body.Value > 1.0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: $"Threshold '{StepUpOptions.ThresholdName}' must be between {options.ThresholdFloor} and 1.0.");
        }

        var entry = new ThresholdEntry(body.Name, verifier.ModelId, body.Value, time.GetUtcNow(), Who(http).Caller.Oid);
        await thresholds.SetAsync(entry, ct);
        return Results.Ok(entry);
    }
}
