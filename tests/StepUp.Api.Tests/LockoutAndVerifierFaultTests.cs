using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StepUp.Core;

namespace StepUp.Api.Tests;

public sealed class LockoutTests : IDisposable
{
    private readonly ApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    private static async Task<HttpResponseMessage> FailOnce(HttpClient client)
    {
        var (id, phrase) = await Flows.ChallengeAsync(client);
        return await Flows.VerifyAsync(client, id, ApiFactory.Speech(9, phrase));
    }

    [Fact]
    public async Task Attempts_DelayAfter5_AndDisableAfter50()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await FailOnce(client)).StatusCode);
        }

        // The sixth attempt is inside the 30 s delay.
        var (id, phrase) = await Flows.ChallengeAsync(client);
        var delayed = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));
        Assert.Equal(HttpStatusCode.TooManyRequests, delayed.StatusCode);
        Assert.Equal("RateLimited", await delayed.CodeAsync());
        Assert.Contains("fallback", await delayed.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // After the delay it is allowed again; keep failing up to 50.
        for (var i = 5; i < 50; i++)
        {
            _api.Time.Advance(TimeSpan.FromSeconds(31));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await FailOnce(client)).StatusCode);
        }

        // 50 consecutive failures: voice is disabled, even for the right voice and long after the delay.
        _api.Time.Advance(TimeSpan.FromMinutes(10));
        var (lastId, lastPhrase) = await Flows.ChallengeAsync(client);
        var disabled = await Flows.VerifyAsync(client, lastId, ApiFactory.Speech(1, lastPhrase));
        Assert.Equal(HttpStatusCode.TooManyRequests, disabled.StatusCode);
    }

    [Fact]
    public async Task Attempts_SuccessResetsTheCounter()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        for (var i = 0; i < 4; i++)
        {
            await FailOnce(client);
        }

        await Flows.StepUpAsync(client, 1);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await FailOnce(client)).StatusCode);
        }
    }

    [Fact]
    public async Task Attempts_QualityRejectsDoNotCountTowardTheLimit()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        var (id, phrase) = await Flows.ChallengeAsync(client);
        for (var i = 0; i < 8; i++)
        {
            var muted = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase, Infrastructure.Verification.FakeQuality.Muted));
            Assert.Equal(HttpStatusCode.BadRequest, muted.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase))).StatusCode);
    }

    [Fact]
    public async Task Reenrolling_ReEnablesAFreshStart()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        for (var i = 0; i < 5; i++)
        {
            await FailOnce(client);
        }

        _api.Time.Advance(TimeSpan.FromSeconds(1));
        await Flows.EnrollAsync(_api, Users.Alice, 1);

        var (id, phrase) = await Flows.ChallengeAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase))).StatusCode);
    }
}

public sealed class VerifierFaultTests
{
    private sealed class ThrowingVerifier : IVerifier
    {
        public string ModelId => "fake-verifier-v1";

        public Task<VerifierResult> AnalyzeAsync(ReadOnlyMemory<byte> wav, string expectedPhrase, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("model crashed");
    }

    private sealed class HangingVerifier : IVerifier
    {
        public string ModelId => "fake-verifier-v1";

        public async Task<VerifierResult> AnalyzeAsync(ReadOnlyMemory<byte> wav, string expectedPhrase, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new VerifierResult(null, null, null);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(ApiFactory Api, HttpClient Client, Guid Id, string Phrase)> ArrangeAsync(IVerifier broken, Dictionary<string, string?>? config = null)
    {
        // Enroll with the working fake verifier first, then swap in the faulty one by using a second factory on the same data.
        var api = new ApiFactory(broken, config);
        var user = Users.Alice;
        var userKey = api.Services.GetRequiredService<IUserKeyHasher>().Hash(StepUp.FakeAuthority.FakeDirectory.FakeTid, user.Oid);
        await api.Services.GetRequiredService<IConsentStore>().GiveAsync(userKey, "v1", Ct);
        await api.Services.GetRequiredService<IVoiceprintStore>().ReplaceAsync(new Voiceprint(userKey, "fake-verifier-v1", [1f, 2f, 3f], api.Time.GetUtcNow()), Ct);
        var client = api.ClientFor(api.TokenFor(user));
        var (id, phrase) = await Flows.ChallengeAsync(client);
        return (api, client, id, phrase);
    }

    [Fact]
    public async Task Verify_FailsClosedOnVerifierError()
    {
        var (api, client, id, phrase) = await ArrangeAsync(new ThrowingVerifier());
        using var _a = api;
        using var _c = client;

        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("VerifierError", await response.CodeAsync());
        Assert.Contains("fallback", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.DoesNotContain("assertion", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.DoesNotContain("model crashed", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_FailsClosedOnTimeout()
    {
        var (api, client, id, phrase) = await ArrangeAsync(new HangingVerifier(), new() { ["StepUp:VerifierTimeout"] = "00:00:00.200" });
        using var _a = api;
        using var _c = client;

        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Timeout", await response.CodeAsync());
        Assert.Contains("fallback", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verifier_ConcurrencyLimitReturns429()
    {
        var (api, client, id, phrase) = await ArrangeAsync(new HangingVerifier(), new() { ["StepUp:VerifierConcurrency"] = "1", ["StepUp:VerifierTimeout"] = "00:00:02" });
        using var _a = api;
        using var _c = client;
        var (id2, phrase2) = await Flows.ChallengeAsync(client);

        var first = Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));
        await Task.Delay(300, Ct);
        var second = await Flows.VerifyAsync(client, id2, ApiFactory.Speech(1, phrase2));

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("RateLimited", await second.CodeAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await first).StatusCode);
    }
}
