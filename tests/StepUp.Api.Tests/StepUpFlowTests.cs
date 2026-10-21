using System.Net;
using System.Net.Http.Json;
using StepUp.Core;
using StepUp.FakeAuthority;
using StepUp.Infrastructure.Verification;
using Microsoft.Extensions.DependencyInjection;

namespace StepUp.Api.Tests;

public sealed class StepUpFlowTests : IDisposable
{
    private readonly ApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    private async Task<HttpClient> EnrolledAlice(byte speaker = 1)
    {
        await Flows.EnrollAsync(_api, Users.Alice, speaker);
        return _api.ClientFor(_api.TokenFor(Users.Alice));
    }

    [Fact]
    public async Task Healthz_IsAnonymous()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/healthz", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReportsEnrollmentAndConsent()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        var before = await (await client.GetAsync("/api/me", Ct)).JsonAsync();
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        var after = await (await client.GetAsync("/api/me", Ct)).JsonAsync();

        Assert.False(before.GetProperty("enrolled").GetBoolean());
        Assert.True(after.GetProperty("enrolled").GetBoolean());
        Assert.True(after.GetProperty("consent").GetBoolean());
    }

    [Fact]
    public async Task Payout_WithoutStepUp_Returns403StepUpRequired()
    {
        using var client = await EnrolledAlice();

        var response = await Flows.PayoutAsync(client, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("StepUpRequired", await response.CodeAsync());
    }

    [Fact]
    public async Task Payout_WithoutBearer_Returns401()
    {
        using var client = _api.CreateClient();

        var response = await Flows.PayoutAsync(client, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VoicePass_IssuesAnAssertionThatPaysOnce()
    {
        using var client = await EnrolledAlice();

        var assertion = await Flows.StepUpAsync(client, 1);
        var first = await Flows.PayoutAsync(client, assertion);
        var replay = await Flows.PayoutAsync(client, assertion);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal("StepUpRequired", await replay.CodeAsync());
    }

    [Fact]
    public async Task Payout_RejectsAssertionIssuedToAnotherUser()
    {
        await Flows.EnrollAsync(_api, Users.Bob, 2);
        using var bob = _api.ClientFor(_api.TokenFor(Users.Bob));
        var bobsAssertion = await Flows.StepUpAsync(bob, 2);
        using var alice = await EnrolledAlice();

        var response = await Flows.PayoutAsync(alice, bobsAssertion);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Payout_RejectsATamperedAssertion()
    {
        using var client = await EnrolledAlice();
        var assertion = await Flows.StepUpAsync(client, 1);
        var response = await Flows.PayoutAsync(client, assertion + "x");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Payout_RejectsChangedPayload()
    {
        using var client = await EnrolledAlice();
        var assertion = await Flows.StepUpAsync(client, 1, payload: "&amount=100&to=acme");

        var changed = await Flows.PayoutAsync(client, assertion, amount: 9000, to: "mallory");
        var original = await Flows.PayoutAsync(client, assertion, amount: 100, to: "acme");

        // A mismatch does not burn the assertion: the user who stepped up for 100/acme can still pay exactly that.
        Assert.Equal(HttpStatusCode.Forbidden, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
    }

    [Fact]
    public async Task Verify_PayoutNeedsItsPayload()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);

        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase), payload: "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bearer_RejectsStepUpAssertion()
    {
        using var client = await EnrolledAlice();
        var assertion = await Flows.StepUpAsync(client, 1);
        using var asBearer = _api.ClientFor(assertion);

        var response = await asBearer.GetAsync("/api/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Challenge_IsSingleUse_OverHttp()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);

        var first = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));
        var second = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
        Assert.Equal("ChallengeConsumed", await second.CodeAsync());
    }

    [Fact]
    public async Task Challenge_ExpiresAfterTheTtl_OverHttp()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);

        _api.Time.Advance(TimeSpan.FromSeconds(61));
        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("ChallengeExpired", await response.CodeAsync());
    }

    [Fact]
    public async Task Challenge_RejectsAnotherUsersChallengeAndAnotherAction()
    {
        await Flows.EnrollAsync(_api, Users.Bob, 2);
        using var bob = _api.ClientFor(_api.TokenFor(Users.Bob));
        using var alice = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(alice);

        var byBob = await Flows.VerifyAsync(bob, id, ApiFactory.Speech(2, phrase));
        var wrongAction = await Flows.VerifyAsync(alice, id, ApiFactory.Speech(1, phrase), action: "delete-account");
        var stillValid = await Flows.VerifyAsync(alice, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.Gone, byBob.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, wrongAction.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task Challenge_RejectsUnknownAction()
    {
        using var client = await EnrolledAlice();

        var response = await client.PostAsJsonAsync("/api/stepup/challenge", new { action = "launch-missiles" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Challenge_RequiresAVoiceprint()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));

        var response = await client.PostAsJsonAsync("/api/stepup/challenge", new { action = "payout" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NoVoiceprint", await response.CodeAsync());
    }

    [Fact]
    public async Task Challenge_RequiresConsent_WhenAVoiceprintExists()
    {
        using var client = await EnrolledAlice();
        await _api.Services.GetRequiredService<IConsentStore>().WithdrawAsync(
            _api.Services.GetRequiredService<IUserKeyHasher>().Hash(FakeDirectory.FakeTid, Users.Alice.Oid), Ct);

        var response = await client.PostAsJsonAsync("/api/stepup/challenge", new { action = "payout" }, Ct);

        Assert.Equal("ConsentMissing", await response.CodeAsync());
    }

    [Fact]
    public async Task Verify_RejectsPhraseMismatch()
    {
        using var client = await EnrolledAlice();
        var (id, _) = await Flows.ChallengeAsync(client);
        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, "bamboo basket beacon bridge"));
        using var analyst = _api.ClientFor(_api.TokenFor(Users.Analyst));

        var attempts = await (await analyst.GetAsync("/api/analyst/attempts", Ct)).JsonAsync();

        Assert.Equal("VerificationFailed", await response.CodeAsync());
        Assert.Equal("PhraseMismatch", attempts[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Verify_FailResponse_IsUniform()
    {
        using var client = await EnrolledAlice();
        var (id1, phrase1) = await Flows.ChallengeAsync(client);
        var wrongSpeaker = await Flows.VerifyAsync(client, id1, ApiFactory.Speech(9, phrase1));
        var (id2, _) = await Flows.ChallengeAsync(client);
        var wrongPhrase = await Flows.VerifyAsync(client, id2, ApiFactory.Speech(1, "bamboo basket beacon bridge"));

        var a = Normalized(await wrongSpeaker.Content.ReadAsStringAsync(Ct));
        var b = Normalized(await wrongPhrase.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongSpeaker.StatusCode);
        Assert.Equal(wrongSpeaker.StatusCode, wrongPhrase.StatusCode);
        Assert.Equal("VerificationFailed", await wrongSpeaker.CodeAsync());
        Assert.Equal("VerificationFailed", await wrongPhrase.CodeAsync());

        // The two failures are indistinguishable: identical bodies once the per-request trace id is removed.
        Assert.Equal(a, b);
        foreach (var body in new[] { a, b })
        {
            Assert.DoesNotContain("score", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("threshold", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PhraseMismatch", body, StringComparison.Ordinal);
            Assert.DoesNotContain("BelowThreshold", body, StringComparison.Ordinal);
        }
    }

    private static string Normalized(string problemJson)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("traceId");
        return node.ToJsonString();
    }

    [Fact]
    public async Task Verify_HighScorePhraseMismatch_NoAssertion()
    {
        using var client = await EnrolledAlice();
        var (id, _) = await Flows.ChallengeAsync(client);

        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, "bamboo basket beacon bridge"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("assertion", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_BelowThreshold_NoAssertion()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);

        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(7, phrase));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("assertion", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_QualityReject_DoesNotConsumeTheChallenge()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);

        var muted = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase, FakeQuality.Muted));
        var retry = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.BadRequest, muted.StatusCode);
        Assert.Equal("Muted", await muted.CodeAsync());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Verify_AfterDelete_ReturnsNoVoiceprint()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/voiceprint", Ct)).StatusCode);
        var verify = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));
        var challenge = await client.PostAsJsonAsync("/api/stepup/challenge", new { action = "payout" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal("NoVoiceprint", await verify.CodeAsync());
        Assert.Equal("NoVoiceprint", await challenge.CodeAsync());
    }

    [Fact]
    public async Task Verify_AfterDeleteWithConsentKept_ReturnsNoVoiceprint()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);
        await _api.Services.GetRequiredService<IVoiceprintStore>().DeleteAsync(
            _api.Services.GetRequiredService<IUserKeyHasher>().Hash(FakeDirectory.FakeTid, Users.Alice.Oid), Ct);

        var verify = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal("NoVoiceprint", await verify.CodeAsync());
    }

    [Fact]
    public async Task Verify_ModelIdMismatch_ReturnsReEnrollRequired()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);
        var user = _api.Services.GetRequiredService<IUserKeyHasher>().Hash(FakeDirectory.FakeTid, Users.Alice.Oid);
        await _api.Services.GetRequiredService<IVoiceprintStore>().ReplaceAsync(new Voiceprint(user, "an-older-model", [1f, 0f], _api.Time.GetUtcNow()), Ct);

        var verify = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));
        var challenge = await client.PostAsJsonAsync("/api/stepup/challenge", new { action = "payout" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal("ReEnrollRequired", await verify.CodeAsync());
        Assert.Equal("ReEnrollRequired", await challenge.CodeAsync());
    }

    [Fact]
    public async Task Verify_RejectsBadAudio()
    {
        using var client = await EnrolledAlice();
        var (id, _) = await Flows.ChallengeAsync(client);

        var garbage = await client.PostAsync($"/api/stepup/verify?challengeId={id}&action=payout&amount=100&to=acme", HttpExtensions.Wav("not a wav, just text, long enough to pass the header length check........"u8.ToArray()), Ct);
        var wrongType = await client.PostAsync($"/api/stepup/verify?challengeId={id}&action=payout&amount=100&to=acme", new StringContent("hello"), Ct);
        var tooBig = await client.PostAsync($"/api/stepup/verify?challengeId={id}&action=payout&amount=100&to=acme", HttpExtensions.Wav(new byte[WavInspector.MaxBytes + 10]), Ct);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, garbage.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
    }

    [Fact]
    public async Task Logs_ContainNoAudioScoreTranscriptOrToken()
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);
        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));
        var assertion = (await response.JsonAsync()).GetProperty("assertion").GetString()!;
        await Flows.PayoutAsync(client, assertion);
        var (id2, phrase2) = await Flows.ChallengeAsync(client);
        await Flows.VerifyAsync(client, id2, ApiFactory.Speech(8, phrase2));

        string[] lines;
        lock (_api.Logs.Lines)
        {
            lines = [.. _api.Logs.Lines];
        }

        // The score is a value an attacker could tune against; the column name in EF's SQL text is not a leak.
        using var analyst = _api.ClientFor(_api.TokenFor(Users.Analyst));
        var attempts = await (await analyst.GetAsync("/api/analyst/attempts", Ct)).JsonAsync();
        var scores = attempts.EnumerateArray().Select(x => x.GetProperty("score").GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture)).ToList();

        string[] forbidden = [phrase, assertion, "eyJ", .. scores];
        var offenders = lines.Where(l => forbidden.Any(f => l.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.NotEmpty(lines);
        Assert.Empty(offenders);
    }
}
