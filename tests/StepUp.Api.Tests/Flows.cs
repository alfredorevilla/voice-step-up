using System.Net;
using System.Net.Http.Json;
using StepUp.FakeAuthority;
using StepUp.Infrastructure.Verification;

namespace StepUp.Api.Tests;

/// <summary>The HTTP steps every test builds on: enroll a speaker, get a challenge, verify, spend the assertion.</summary>
public static class Flows
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task EnrollAsync(ApiFactory api, FakeUser user, byte speaker)
    {
        using var client = api.ClientFor(api.FreshMfa(user));
        var consent = await client.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct);
        Assert.True(consent.IsSuccessStatusCode || consent.StatusCode == HttpStatusCode.Conflict);
        var start = await client.PostAsync("/api/enrollment/start", null, Ct);
        start.EnsureSuccessStatusCode();
        var body = await start.JsonAsync();
        var id = body.GetProperty("enrollmentId").GetGuid();
        foreach (var phrase in body.GetProperty("phrases").EnumerateArray().Select(p => p.GetString()!))
        {
            var sample = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(speaker, phrase)), Ct);
            sample.EnsureSuccessStatusCode();
        }

        (await client.PostAsync($"/api/enrollment/complete?enrollmentId={id}", null, Ct)).EnsureSuccessStatusCode();
    }

    public static async Task<(Guid Id, string Phrase)> ChallengeAsync(HttpClient client, string action = "payout")
    {
        var response = await client.PostAsJsonAsync("/api/stepup/challenge", new { action }, Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.JsonAsync();
        return (body.GetProperty("challengeId").GetGuid(), body.GetProperty("phrase").GetString()!);
    }

    public static Task<HttpResponseMessage> VerifyAsync(HttpClient client, Guid challengeId, byte[] audio, string action = "payout", string payload = "&amount=100&to=acme") =>
        client.PostAsync($"/api/stepup/verify?challengeId={challengeId}&action={action}{payload}", HttpExtensions.Wav(audio), Ct);

    /// <summary>A full successful voice step-up; returns the assertion.</summary>
    public static async Task<string> StepUpAsync(HttpClient client, byte speaker, string action = "payout", string payload = "&amount=100&to=acme")
    {
        var (id, phrase) = await ChallengeAsync(client, action);
        var response = await VerifyAsync(client, id, ApiFactory.Speech(speaker, phrase), action, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.JsonAsync()).GetProperty("assertion").GetString()!;
    }

    public static Task<HttpResponseMessage> PayoutAsync(HttpClient client, string? assertion, decimal amount = 100, string to = "acme") =>
        Send(client, assertion, amount, to);

    private static Task<HttpResponseMessage> Send(HttpClient client, string? assertion, decimal amount, string to)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payouts") { Content = JsonContent.Create(new { amount, to }) };
        if (assertion is not null)
        {
            request.Headers.Add("X-StepUp-Assertion", assertion);
        }

        return client.SendAsync(request, Ct);
    }
}
