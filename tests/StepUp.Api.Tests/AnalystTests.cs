using System.Net;
using System.Net.Http.Json;

namespace StepUp.Api.Tests;

public sealed class AnalystTests : IDisposable
{
    private readonly ApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    private HttpClient AnalystClient() => _api.ClientFor(_api.TokenFor(Users.Analyst));

    [Theory]
    [InlineData("GET", "/api/analyst/attempts")]
    [InlineData("GET", "/api/analyst/thresholds")]
    [InlineData("PUT", "/api/analyst/thresholds")]
    public async Task Analyst_EndpointsRejectUserRole(string method, string path)
    {
        using var user = _api.ClientFor(_api.TokenFor(Users.Alice));
        using var anonymous = _api.CreateClient();
        HttpContent? Body() => method == "PUT" ? JsonContent.Create(new { name = "stepup", value = 0.5 }) : null;

        var asUser = await user.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = Body() }, Ct);
        var asAnonymous = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = Body() }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }

    [Fact]
    public async Task Analyst_SeesScoreResultAndReasonPerAttempt()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var alice = _api.ClientFor(_api.TokenFor(Users.Alice));
        await Flows.StepUpAsync(alice, 1);
        var (id, phrase) = await Flows.ChallengeAsync(alice);
        await Flows.VerifyAsync(alice, id, ApiFactory.Speech(9, phrase));
        using var analyst = AnalystClient();

        var attempts = await (await analyst.GetAsync("/api/analyst/attempts", Ct)).JsonAsync();
        var fail = attempts.EnumerateArray().Single(a => a.GetProperty("result").GetString() == "Fail");
        var pass = attempts.EnumerateArray().Single(a => a.GetProperty("result").GetString() == "Pass");

        Assert.Equal("BelowThreshold", fail.GetProperty("reason").GetString());
        Assert.True(fail.GetProperty("score").GetDouble() < fail.GetProperty("threshold").GetDouble());
        Assert.True(pass.GetProperty("score").GetDouble() >= pass.GetProperty("threshold").GetDouble());
        Assert.Equal(JsonValueKindNull, pass.GetProperty("reason").ValueKind);
        Assert.DoesNotContain(Users.Alice.Oid, attempts.GetRawText(), StringComparison.Ordinal);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task Thresholds_AreSeededAndOnlyChangeThroughTheApiWithinBounds()
    {
        using var analyst = AnalystClient();
        var seeded = await (await analyst.GetAsync("/api/analyst/thresholds", Ct)).JsonAsync();

        var tooHigh = await analyst.PutAsJsonAsync("/api/analyst/thresholds", new { name = "stepup", value = 1.5 }, Ct);
        var tooLow = await analyst.PutAsJsonAsync("/api/analyst/thresholds", new { name = "stepup", value = 0.01 }, Ct);
        var nan = await analyst.PutAsJsonAsync("/api/analyst/thresholds", new { name = "other", value = 0.5 }, Ct);
        var ok = await analyst.PutAsJsonAsync("/api/analyst/thresholds", new { name = "stepup", value = 0.9 }, Ct);
        var after = await (await analyst.GetAsync("/api/analyst/thresholds", Ct)).JsonAsync();

        Assert.Equal("seed", seeded[0].GetProperty("updatedBy").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(2, after.GetArrayLength());
        Assert.Equal(Users.Analyst.Oid, after[0].GetProperty("updatedBy").GetString());
        Assert.Equal(0.9, after[0].GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task Thresholds_ComeFromTheStore_NotFromCode()
    {
        await Flows.EnrollAsync(_api, Users.Alice, 1);
        using var alice = _api.ClientFor(_api.TokenFor(Users.Alice));
        using var analyst = AnalystClient();
        Assert.NotEmpty(await Flows.StepUpAsync(alice, 1));

        await analyst.PutAsJsonAsync("/api/analyst/thresholds", new { name = "stepup", value = 1.0 }, Ct);
        var (id, phrase) = await Flows.ChallengeAsync(alice);
        var strict = await Flows.VerifyAsync(alice, id, ApiFactory.Speech(1, phrase));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, strict.StatusCode);
    }
}
