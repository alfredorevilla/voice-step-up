using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StepUp.Api.Auth;
using StepUp.Verifier.Tests;

namespace StepUp.Api.Tests;

public sealed class VerifierModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://localhost:9000")]
    [InlineData("http://[::1]:9000")]
    [InlineData("http://verifier:8080")]
    [InlineData("https://verifier.example.com")]
    public void EndpointGuard_AcceptsLoopbackComposeNamesAndHttps(string address)
    {
        Assert.Equal(new Uri(address), VerifierEndpointGuard.Require(address));
    }

    [Theory]
    [InlineData("http://verifier.example.com")]
    [InlineData("http://10.1.2.3:8080")]
    [InlineData("ftp://verifier")]
    [InlineData("verifier:8080")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void EndpointGuard_RefusesPlainHttpAcrossNetworksAndNonHttpAddresses(string? address)
    {
        Assert.Throws<InvalidOperationException>(() => VerifierEndpointGuard.Require(address));
    }

    private static Exception StartupFailure(Dictionary<string, string?> config)
    {
        var root = Path.Combine(Path.GetTempPath(), "stepup-mode-" + Guid.NewGuid().ToString("N"));
        var values = new Dictionary<string, string?>
        {
            ["Demo:Enabled"] = "true",
            ["Auth:Mode"] = "Fake",
            ["Storage:DatabasePath"] = Path.Combine(root, "stepup.db"),
            ["Storage:KeyDirectory"] = Path.Combine(root, "keys"),
        };
        foreach (var (key, value) in config)
        {
            values[key] = value;
        }

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSettings(values);
        });
        try
        {
            return Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static bool Mentions(Exception error, string text)
    {
        for (var e = error; e is not null; e = e.InnerException!)
        {
            if (e.Message.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }

            if (e.InnerException is null)
            {
                break;
            }
        }

        return false;
    }

    [Fact]
    public void Startup_WithAnUnknownVerifierMode_Fails()
    {
        var error = StartupFailure(new() { ["Verifier:Mode"] = "Quantum" });

        Assert.True(Mentions(error, "Verifier:Mode must be"), error.ToString());
    }

    [Fact]
    public void Startup_InSherpaModeWithoutTheModels_FailsAndPointsAtTheFetchScript()
    {
        var error = StartupFailure(new()
        {
            ["Verifier:Mode"] = "Sherpa",
            ["Verifier:Sherpa:ModelsDir"] = Path.Combine(Path.GetTempPath(), "no-models-" + Guid.NewGuid().ToString("N")),
        });

        Assert.True(Mentions(error, "fetch-models.sh"), error.ToString());
    }

    [Fact]
    public void Startup_InHttpModeWithoutAModelId_Fails()
    {
        var error = StartupFailure(new() { ["Verifier:Mode"] = "Http", ["Verifier:Http:BaseAddress"] = "http://127.0.0.1:1" });

        Assert.True(Mentions(error, "Verifier:Http:ModelId"), error.ToString());
    }

    [Fact]
    public void Startup_InHttpModeWithPlainHttpAcrossTheInternet_Fails()
    {
        var error = StartupFailure(new()
        {
            ["Verifier:Mode"] = "Http",
            ["Verifier:Http:BaseAddress"] = "http://verifier.example.com",
            ["Verifier:Http:ModelId"] = "m",
        });

        Assert.True(Mentions(error, "must use https"), error.ToString());
    }

    [Fact]
    public async Task HttpMode_RunsEnrollmentStepUpAndPayoutThroughARemoteVerifier()
    {
        await using var stub = await StubVerifierService.StartAsync(requireKey: true);
        using var api = new ApiFactory(config: new()
        {
            ["Verifier:Mode"] = "Http",
            ["Verifier:Http:BaseAddress"] = stub.Address.ToString(),
            ["Verifier:Http:ModelId"] = "fake-verifier-v1",
            ["Verifier:Http:ApiKey"] = StubVerifierService.Key,
        });
        await Flows.EnrollAsync(api, Users.Alice, 1);
        using var client = api.ClientFor(api.TokenFor(Users.Alice));

        var assertion = await Flows.StepUpAsync(client, 1);
        var payout = await Flows.PayoutAsync(client, assertion);

        Assert.Equal(HttpStatusCode.OK, payout.StatusCode);
        Assert.True(stub.Calls >= 4, $"the remote verifier was called {stub.Calls} times");
        Assert.Equal(StubVerifierService.Key, stub.LastKeyHeader);
    }

    [Fact]
    public async Task HttpMode_WhenTheRemoteVerifierIsDown_AnswersWithTheFallbackNotAPass()
    {
        await using var stub = await StubVerifierService.StartAsync();
        using var api = new ApiFactory(config: new()
        {
            ["Verifier:Mode"] = "Http",
            ["Verifier:Http:BaseAddress"] = stub.Address.ToString(),
            ["Verifier:Http:ModelId"] = "fake-verifier-v1",
        });
        await Flows.EnrollAsync(api, Users.Alice, 1);
        using var client = api.ClientFor(api.TokenFor(Users.Alice));
        var (id, phrase) = await Flows.ChallengeAsync(client);
        stub.Override = _ => Microsoft.AspNetCore.Http.Results.StatusCode(503);

        var response = await Flows.VerifyAsync(client, id, ApiFactory.Speech(1, phrase));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("fallback", await response.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);
    }
}
