using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using StepUp.Core;
using StepUp.FakeAuthority;

namespace StepUp.Api.Tests;

public sealed class AuthModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<Program> Configured(string environment, Dictionary<string, string?> config) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSettings(config);
        });

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Staging", "true")]
    [InlineData("Development", "false")]
    [InlineData("Production", "false")]
    public void Startup_RefusesFakeModeOutsideDemo(string environment, string demoFlag)
    {
        var temp = Path.Combine(Path.GetTempPath(), "stepup-guard-" + Guid.NewGuid().ToString("N"));
        using var factory = Configured(environment, new()
        {
            ["Auth:Mode"] = "Fake",
            ["Verifier:Mode"] = "Fake",
            ["Demo:Enabled"] = demoFlag,
            ["Storage:DatabasePath"] = Path.Combine(temp, "db"),
            ["Storage:KeyDirectory"] = Path.Combine(temp, "keys"),
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Demo:Enabled", ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000")]
    public void Startup_RefusesEntraModeWithoutAGuidTenantId(string? tenant)
    {
        var temp = Path.Combine(Path.GetTempPath(), "stepup-guard-" + Guid.NewGuid().ToString("N"));
        var config = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Entra",
            ["AzureAd:Authority"] = "https://login.microsoftonline.com/common/v2.0",
            ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000000",
            ["AzureAd:Audience"] = "api://voice-step-up",
            ["Verifier:Mode"] = "Fake",
            ["Demo:Enabled"] = "true",
            ["Storage:DatabasePath"] = Path.Combine(temp, "db"),
            ["Storage:KeyDirectory"] = Path.Combine(temp, "keys"),
        };
        if (tenant is not null)
        {
            config["AzureAd:TenantId"] = tenant;
        }

        using var factory = Configured("Development", config);

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("AzureAd:TenantId", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_RefusesTheFakeVerifierOutsideDemo()
    {
        var temp = Path.Combine(Path.GetTempPath(), "stepup-guard-" + Guid.NewGuid().ToString("N"));
        using var factory = Configured("Production", new()
        {
            ["Auth:Mode"] = "Entra",
            ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
            ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000000",
            ["AzureAd:Audience"] = "api://voice-step-up",
            ["Verifier:Mode"] = "Fake",
            ["Demo:Enabled"] = "true",
            ["Storage:DatabasePath"] = Path.Combine(temp, "db"),
            ["Storage:KeyDirectory"] = Path.Combine(temp, "keys"),
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Verifier:Mode=Fake", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_RefusesAnUnknownAuthMode()
    {
        using var factory = Configured("Development", new() { ["Auth:Mode"] = "None" });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private const string EntraIssuerShape = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0";

    // Entra mode with a stand-in for Entra's own key set, so the test never touches the network.
    private sealed class EntraModeApi : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "stepup-entra-" + Guid.NewGuid().ToString("N"));
        public FakeTokenMinter EntraStandIn { get; } = new(EntraIssuerShape, "api://voice-step-up");
        public WebApplicationFactory<Program> Factory { get; }

        public EntraModeApi()
        {
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSettings(new Dictionary<string, string?>
                {
                    ["Auth:Mode"] = "Entra",
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000000",
                    ["AzureAd:Audience"] = "api://voice-step-up",
                    ["Verifier:Mode"] = "Fake",
                    ["Demo:Enabled"] = "true",
                    ["Storage:DatabasePath"] = Path.Combine(_root, "db"),
                    ["Storage:KeyDirectory"] = Path.Combine(_root, "keys"),
                });
                b.ConfigureTestServices(s => s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = EntraIssuerShape };
                    configuration.SigningKeys.Add(EntraStandIn.SigningKey);
                    o.Configuration = configuration;
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);

                    // Identity.Web's own issuer validator fetches Entra metadata over the network, which a unit test must not do.
                    // It is replaced by an exact issuer check here; the real one is exercised live in M5 T5.4.
                    o.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
                        issuer == EntraIssuerShape ? issuer : throw new SecurityTokenInvalidIssuerException("issuer");
                }));
            });
        }

        public void Dispose()
        {
            Factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<HttpStatusCode> MeAsync(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/me", Ct)).StatusCode;
    }

    private static MintRequest Request(string tid = "00000000-0000-0000-0000-000000000000") =>
        new(FakeDirectory.Users["alice"].Oid, tid, ["pwd"], DateTimeOffset.UtcNow);

    [Fact]
    public async Task EntraMode_RejectsFakeIssuerToken()
    {
        using var api = new EntraModeApi();
        using var client = api.Factory.CreateClient();
        var fakeAuthority = new FakeTokenMinter(ApiFactory.Issuer, "api://voice-step-up");

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, fakeAuthority.Mint(Request())));
    }

    [Fact]
    public async Task Bearer_RejectsStepUpAssertion_InEntraMode()
    {
        using var api = new EntraModeApi();
        using var client = api.Factory.CreateClient();
        var assertion = api.Factory.Services.GetRequiredService<StepUpAssertionService>()
            .Issue(new AssertionBinding(FakeDirectory.Users["alice"].Oid, "00000000-0000-0000-0000-000000000000", "payout", null)).Token;

        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, api.EntraStandIn.Mint(Request())));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, assertion));
    }

    [Fact]
    public async Task EntraMode_AcceptsATokenFromItsOwnIssuer_WithTheSameChecks()
    {
        using var api = new EntraModeApi();
        using var client = api.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, api.EntraStandIn.Mint(Request())));
    }

    [Fact]
    public async Task EntraMode_RejectsATokenForAnotherTenantAndAppOnlyTokens()
    {
        using var api = new EntraModeApi();
        using var client = api.Factory.CreateClient();

        var otherTenant = api.EntraStandIn.Mint(Request(tid: "00000000-0000-0000-0000-0000000000aa"));
        var appOnly = api.EntraStandIn.Mint(Request() with { IdTyp = "app", Scope = null });

        // Rejected either by Identity.Web's tenant-scoped validation (401) or by the tid policy (403); never accepted.
        Assert.True(await MeAsync(client, otherTenant) is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.True(await MeAsync(client, appOnly) is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FakeMode_RejectsTokensWithoutTheScopeAndAppOnlyTokens()
    {
        using var api = new ApiFactory();
        using var client = api.CreateClient();
        var alice = FakeDirectory.Users["alice"];

        var noScope = api.Minter.Mint(new MintRequest(alice.Oid, FakeDirectory.FakeTid, ["pwd"], api.Time.GetUtcNow(), Scope: null));
        var appOnly = api.Minter.Mint(new MintRequest(alice.Oid, FakeDirectory.FakeTid, ["pwd"], api.Time.GetUtcNow(), IdTyp: "app"));
        var noTid = api.Minter.Mint(new MintRequest(alice.Oid, "", ["pwd"], api.Time.GetUtcNow()));

        Assert.Equal(HttpStatusCode.Forbidden, await MeAsync(client, noScope));
        Assert.True(await MeAsync(client, appOnly) is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, await MeAsync(client, noTid));
    }

    [Fact]
    public async Task FakeMode_RejectsATokenSignedByAnotherKeyOrForAnotherAudience()
    {
        using var api = new ApiFactory();
        using var client = api.CreateClient();
        var alice = FakeDirectory.Users["alice"];
        var stranger = new FakeTokenMinter(ApiFactory.Issuer, ApiFactory.Audience, api.Time);
        var wrongAudience = new FakeTokenMinter(ApiFactory.Issuer, "api://somebody-else", api.Time);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, stranger.Mint(Request())));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, wrongAudience.Mint(Request())));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, api.TokenFor(alice)));
    }

    [Fact]
    public async Task Cors_RejectsOtherOrigins()
    {
        using var api = new ApiFactory();
        using var client = api.CreateClient();

        HttpRequestMessage Preflight(string origin) => new(HttpMethod.Options, "/api/me")
        {
            Headers = { { "Origin", origin }, { "Access-Control-Request-Method", "GET" }, { "Access-Control-Request-Headers", "authorization" } },
        };

        var allowed = await client.SendAsync(Preflight(ApiFactory.SpaOrigin), Ct);
        var evil = await client.SendAsync(Preflight("https://evil.example"), Ct);
        var simple = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/healthz") { Headers = { { "Origin", "https://evil.example" } } }, Ct);

        Assert.Contains(ApiFactory.SpaOrigin, allowed.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.False(evil.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(simple.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task RateLimits_ApplyPerUser_AndGeneralCeilingHolds()
    {
        using var api = new ApiFactory(config: new() { ["StepUp:GeneralPerMinute"] = "5", ["StepUp:ChallengePerMinute"] = "2" });
        using var alice = api.ClientFor(api.TokenFor(FakeDirectory.Users["alice"]));
        using var bob = api.ClientFor(api.TokenFor(FakeDirectory.Users["bob"]));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            statuses.Add((await alice.GetAsync("/api/me", Ct)).StatusCode);
        }

        // Alice used her ceiling; Bob is a different partition and is unaffected.
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Enrollment_StartIsRateLimitedPerUser()
    {
        using var api = new ApiFactory(config: new() { ["StepUp:ChallengePerMinute"] = "2" });
        using var client = api.ClientFor(api.FreshMfa(FakeDirectory.Users["alice"]));
        await client.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct);

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            codes.Add((await client.PostAsync("/api/enrollment/start", null, Ct)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public void KeyMaterial_IsNotInTheRepoOrAppsettings()
    {
        var appsettings = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Assert.DoesNotContain("TenantId", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientId", appsettings, StringComparison.Ordinal);
    }
}
