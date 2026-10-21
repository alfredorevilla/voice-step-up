using System.Net;
using System.Net.Http.Json;
using StepUp.Core;
using StepUp.Infrastructure.Verification;

namespace StepUp.Api.Tests;

public sealed class EnrollmentAndFallbackTests : IDisposable
{
    private readonly ApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    private async Task<(HttpClient Client, Guid Id, string[] Phrases)> StartedAsync(FakeAuthority.FakeUser? user = null)
    {
        var client = _api.ClientFor(_api.FreshMfa(user ?? Users.Alice));
        var consent = await client.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct);
        Assert.True(consent.IsSuccessStatusCode || consent.StatusCode == HttpStatusCode.Conflict);
        var body = await (await client.PostAsync("/api/enrollment/start", null, Ct)).JsonAsync();
        return (client, body.GetProperty("enrollmentId").GetGuid(), [.. body.GetProperty("phrases").EnumerateArray().Select(p => p.GetString()!)]);
    }

    [Fact]
    public async Task Enroll_RequiresConsent()
    {
        using var client = _api.ClientFor(_api.FreshMfa(Users.Alice));

        var response = await client.PostAsync("/api/enrollment/start", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ConsentMissing", await response.CodeAsync());
    }

    [Fact]
    public async Task Consent_CannotBeGivenTwice()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        await client.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct);

        var again = await client.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Enroll_RequiresFreshMfa()
    {
        using var stale = _api.ClientFor(_api.TokenFor(Users.Alice, ["pwd", "mfa"], authAge: TimeSpan.FromHours(2)));
        await stale.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct);
        using var noMfa = _api.ClientFor(_api.TokenFor(Users.Alice));

        var staleStart = await stale.PostAsync("/api/enrollment/start", null, Ct);
        var noMfaStart = await noMfa.PostAsync("/api/enrollment/start", null, Ct);
        var (client, id, _) = await StartedAsync();
        using var _c = client;
        var staleComplete = await stale.PostAsync($"/api/enrollment/complete?enrollmentId={id}", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, staleStart.StatusCode);
        Assert.Equal("FreshMfaRequired", await staleStart.CodeAsync());
        Assert.Equal("FreshMfaRequired", await noMfaStart.CodeAsync());
        Assert.Equal("FreshMfaRequired", await staleComplete.CodeAsync());
    }

    [Fact]
    public async Task Enroll_RejectsOtherUsersEnrollmentId()
    {
        var (alice, id, phrases) = await StartedAsync();
        using var _a = alice;
        using var bob = _api.ClientFor(_api.FreshMfa(Users.Bob));

        var sample = await bob.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(2, phrases[0])), Ct);
        var complete = await bob.PostAsync($"/api/enrollment/complete?enrollmentId={id}", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, sample.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, complete.StatusCode);
    }

    [Fact]
    public async Task Enroll_RejectsInconsistentSpeakers()
    {
        var (client, id, phrases) = await StartedAsync();
        using var _c = client;
        var speakers = new byte[] { 1, 2, 3 };
        for (var i = 0; i < phrases.Length; i++)
        {
            (await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(speakers[i], phrases[i])), Ct)).EnsureSuccessStatusCode();
        }

        var complete = await client.PostAsync($"/api/enrollment/complete?enrollmentId={id}", null, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, complete.StatusCode);
        Assert.False((await (await client.GetAsync("/api/me", Ct)).JsonAsync()).GetProperty("enrolled").GetBoolean());
    }

    [Fact]
    public async Task Enroll_NeedsAllSamples()
    {
        var (client, id, phrases) = await StartedAsync();
        using var _c = client;
        await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(1, phrases[0])), Ct);

        var complete = await client.PostAsync($"/api/enrollment/complete?enrollmentId={id}", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, complete.StatusCode);
    }

    [Fact]
    public async Task Enroll_ChecksThePhraseAndTheAudioQuality()
    {
        var (client, id, phrases) = await StartedAsync();
        using var _c = client;

        var wrongWords = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(1, "bamboo basket beacon bridge")), Ct);
        var muted = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(1, phrases[0], FakeQuality.Muted)), Ct);
        var tooLong = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(new byte[WavInspector.MaxBytes + 1]), Ct);

        Assert.Equal("PhraseMismatch", await wrongWords.CodeAsync());
        Assert.Equal("Muted", await muted.CodeAsync());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLong.StatusCode);
    }

    [Fact]
    public async Task Reenroll_ReplacesTheVoiceprint_AndKeepsOneRow()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        await Flows.EnrollAsync(_api, Users.Alice, 5);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));

        var oldVoice = await Flows.ChallengeAsync(client);
        var oldResponse = await Flows.VerifyAsync(client, oldVoice.Id, ApiFactory.Speech(1, oldVoice.Phrase));
        var newVoice = await Flows.ChallengeAsync(client);
        var newResponse = await Flows.VerifyAsync(client, newVoice.Id, ApiFactory.Speech(5, newVoice.Phrase));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, oldResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newResponse.StatusCode);
    }

    [Fact]
    public async Task Fallback_AcceptsFreshMfa_AndLogsItAsFallback()
    {
        using var client = _api.ClientFor(_api.FreshMfa(Users.Alice));

        var response = await Flows.PayoutAsync(client, null);
        using var analyst = _api.ClientFor(_api.TokenFor(Users.Analyst));
        var attempts = await (await analyst.GetAsync("/api/analyst/attempts", Ct)).JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Fallback", attempts[0].GetProperty("mode").GetString());
        Assert.Equal("Pass", attempts[0].GetProperty("result").GetString());
    }

    [Fact]
    public async Task Fallback_AcceptsNgcmfa()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice, ["pwd", "ngcmfa"]));

        Assert.Equal(HttpStatusCode.OK, (await Flows.PayoutAsync(client, null)).StatusCode);
    }

    [Fact]
    public async Task Fallback_RejectsStaleAuthTime()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice, ["pwd", "mfa"], authAge: TimeSpan.FromMinutes(11)));

        var response = await Flows.PayoutAsync(client, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("StepUpRequired", await response.CodeAsync());
    }

    [Fact]
    public async Task Fallback_RejectsMissingAuthTime()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice, ["pwd", "mfa"], noAuthTime: true));

        Assert.Equal(HttpStatusCode.Forbidden, (await Flows.PayoutAsync(client, null)).StatusCode);
    }

    [Fact]
    public async Task Fallback_RejectsAmrWithoutMfa()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice, ["pwd"], authAge: TimeSpan.FromSeconds(5)));

        Assert.Equal(HttpStatusCode.Forbidden, (await Flows.PayoutAsync(client, null)).StatusCode);
    }

    [Fact]
    public async Task Fallback_BecomesStaleWhenTimePasses()
    {
        using var client = _api.ClientFor(_api.FreshMfa(Users.Alice));
        Assert.Equal(HttpStatusCode.OK, (await Flows.PayoutAsync(client, null)).StatusCode);

        _api.Time.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(HttpStatusCode.Forbidden, (await Flows.PayoutAsync(client, null)).StatusCode);
    }
}
