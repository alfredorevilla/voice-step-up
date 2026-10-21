using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StepUp.Core;
using StepUp.FakeAuthority;

namespace StepUp.Api.Tests;

/// <summary>Every refused attempt is recorded with a reason code, so the detections in the threat model have data.</summary>
public sealed class RefusalLoggingTests : IDisposable
{
    private readonly ApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    private UserKey KeyOf(FakeUser user) =>
        _api.Services.GetRequiredService<IUserKeyHasher>().Hash(FakeDirectory.FakeTid, user.Oid);

    private async Task<List<Attempt>> AttemptsOfAsync(FakeUser user)
    {
        var key = KeyOf(user);
        var all = await _api.Services.GetRequiredService<IAttemptLog>().ListAsync(new AttemptFilter(null, null, null, 0, 200), Ct);
        return [.. all.Where(a => a.User == key)];
    }

    [Fact]
    public async Task Verify_NoVoiceprint_IsLoggedAsRefused()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));

        var response = await Flows.VerifyAsync(client, Guid.NewGuid(), ApiFactory.Speech(1, "a b c d"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var row = Assert.Single(await AttemptsOfAsync(Users.Alice));
        Assert.Equal(AttemptResult.Refused, row.Result);
        Assert.Equal(ReasonCode.NoVoiceprint, row.Reason);
        Assert.Equal(AttemptMode.Voice, row.Mode);
    }

    [Fact]
    public async Task Verify_ExpiredChallenge_IsLoggedAsRefused()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        var (id, phrase) = await Flows.ChallengeAsync(client);
        _api.Time.Advance(TimeSpan.FromMinutes(5));

        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Contains(await AttemptsOfAsync(Users.Alice), a => a.Result == AttemptResult.Refused && a.Reason == ReasonCode.ChallengeExpired);
    }

    [Fact]
    public async Task Verify_WhileLockedOut_IsLogged_AndDoesNotExtendTheFailureCount()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        for (var i = 0; i < 5; i++)
        {
            var (failId, failPhrase) = await Flows.ChallengeAsync(client);
            await Flows.VerifyAsync(client, failId, ApiFactory.Speech(9, failPhrase));
        }

        var (id, phrase) = await Flows.ChallengeAsync(client);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase))).StatusCode);
        }

        var attempts = await AttemptsOfAsync(Users.Alice);
        var failures = await _api.Services.GetRequiredService<IAttemptLog>().GetFailureStateAsync(KeyOf(Users.Alice), DateTimeOffset.MinValue, Ct);
        Assert.Equal(3, attempts.Count(a => a.Result == AttemptResult.Refused && a.Reason == ReasonCode.RateLimited));
        Assert.Equal(5, failures.ConsecutiveFailures);
    }

    [Fact]
    public async Task Payout_WithoutStepUp_IsLoggedAsRefused()
    {
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));

        var response = await Flows.PayoutAsync(client, assertion: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var row = Assert.Single(await AttemptsOfAsync(Users.Alice));
        Assert.Equal(AttemptResult.Refused, row.Result);
        Assert.Equal(ReasonCode.StepUpRequired, row.Reason);
    }

    [Fact]
    public async Task Payout_ReplayedAndTamperedAssertions_AreLoggedWithTheirOwnReason()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        var assertion = await Flows.StepUpAsync(client, 1);
        Assert.Equal(HttpStatusCode.OK, (await Flows.PayoutAsync(client, assertion)).StatusCode);

        var replay = await Flows.PayoutAsync(client, assertion);
        var tampered = await Flows.PayoutAsync(client, assertion[..^3] + "AAA");

        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, tampered.StatusCode);
        var attempts = await AttemptsOfAsync(Users.Alice);
        Assert.Contains(attempts, a => a.Result == AttemptResult.Refused && a.Reason == ReasonCode.AssertionReplayed);
        Assert.Contains(attempts, a => a.Result == AttemptResult.Refused && a.Reason == ReasonCode.AssertionInvalid);
    }

    [Fact]
    public async Task Withdrawal_PseudonymisesAttemptsButKeepsTheRows()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var client = _api.ClientFor(_api.TokenFor(Users.Alice));
        await Flows.StepUpAsync(client, 1);
        var before = (await AttemptsOfAsync(Users.Alice)).Count;

        (await client.DeleteAsync("/api/voiceprint", Ct)).EnsureSuccessStatusCode();

        Assert.True(before >= 1);
        Assert.Empty(await AttemptsOfAsync(Users.Alice));
        var all = await _api.Services.GetRequiredService<IAttemptLog>().ListAsync(new AttemptFilter(null, null, null, 0, 200), Ct);
        Assert.Equal(before, all.Count);
        Assert.All(all, a => Assert.StartsWith("anon-", a.User.Value, StringComparison.Ordinal));
        Assert.Contains(all, a => a.Score is not null);
    }
}
