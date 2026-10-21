using System.Net;
using System.Net.Http.Json;
using StepUp.Core;
using StepUp.Infrastructure.Verification;

namespace StepUp.Api.Tests;

/// <summary>
/// The spec's edge cases that no earlier milestone covered at the HTTP level (M7 T7.1): bad audio of every kind, a
/// replayed old recording, a genuine voice that scores below the threshold (illness), and the limits of the design.
/// </summary>
public sealed class EdgeCaseTests : IDisposable
{
    private readonly ApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    private async Task<HttpClient> EnrolledAlice()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        return _api.ClientFor(_api.TokenFor(Users.Alice));
    }

    [Theory]
    [InlineData(FakeQuality.Muted, "Muted")]
    [InlineData(FakeQuality.TooShort, "TooShort")]
    [InlineData(FakeQuality.Noisy, "Noisy")]
    public async Task Verify_BadAudio_IsNamedKeepsTheChallengeAndNeverCountsAsAFailure(FakeQuality quality, string code)
    {
        using var client = await EnrolledAlice();
        var (id, phrase) = await Flows.ChallengeAsync(client);

        // One more bad recording than the failures that trigger the delay.
        for (var i = 0; i < new LockoutOptions().DelayAfterFailures + 1; i++)
        {
            var rejected = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase, quality));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(code, await rejected.CodeAsync());
            Assert.DoesNotContain("assertion", await rejected.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        var retry = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Verify_ReplayingAnOldRecordingAgainstANewChallenge_IsRefused()
    {
        using var client = await EnrolledAlice();
        var (firstId, firstPhrase) = await Flows.ChallengeAsync(client);
        var recording = ApiFactory.Speech(1, firstPhrase);
        Assert.Equal(HttpStatusCode.OK, (await Flows.VerifyAsync(client, firstId, recording)).StatusCode);

        // The attacker has the genuine recording and plays it for the next challenge.
        var (secondId, secondPhrase) = await Flows.ChallengeAsync(client);
        Assert.NotEqual(firstPhrase, secondPhrase);
        var replayed = await Flows.VerifyAsync(client, secondId, recording);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, replayed.StatusCode);
        Assert.Equal("VerificationFailed", await replayed.CodeAsync());
        Assert.DoesNotContain("assertion", await replayed.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Illness_BorderlineScore_OffersTheFallbackWithoutLockingVoice_AndReEnrollmentAfterMfaWorks()
    {
        using var client = await EnrolledAlice();
        using var analyst = _api.ClientFor(_api.TokenFor(Users.Analyst));
        var seeded = (await (await analyst.GetAsync("/api/analyst/thresholds", Ct)).JsonAsync())[0].GetProperty("value").GetDouble();

        // A cold changes the voice: the same speaker now scores below the bar. A threshold of 1.0 stands in for that.
        Assert.Equal(HttpStatusCode.OK, (await analyst.PutAsJsonAsync("/api/analyst/thresholds", new { name = "stepup", value = 1.0 }, Ct)).StatusCode);
        var (id, phrase) = await Flows.ChallengeAsync(client);
        var borderline = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, borderline.StatusCode);
        Assert.Equal("VerificationFailed", await borderline.CodeAsync());
        Assert.Contains("fallback", await borderline.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Not a lockout: with the bar back where it was, the very next try passes with no delay.
        Assert.Equal(HttpStatusCode.OK, (await analyst.PutAsJsonAsync("/api/analyst/thresholds", new { name = "stepup", value = seeded }, Ct)).StatusCode);
        var (nextId, nextPhrase) = await Flows.ChallengeAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await Flows.VerifyAsync(client, nextId, ApiFactory.Speech(1, nextPhrase))).StatusCode);

        // The fallback: fresh multi-factor sign-in pays out without any voice assertion.
        using var mfa = _api.ClientFor(_api.FreshMfa(Users.Alice));
        Assert.Equal(HttpStatusCode.OK, (await Flows.PayoutAsync(mfa, null)).StatusCode);

        // Re-enrolling after multi-factor sign-in replaces the voiceprint: the new voice (speaker 2) now passes.
        await Flows.EnrollAsync(_api, Users.Alice, 2);
        var (lastId, lastPhrase) = await Flows.ChallengeAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await Flows.VerifyAsync(client, lastId, ApiFactory.Speech(2, lastPhrase))).StatusCode);
    }
}
