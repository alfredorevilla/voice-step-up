using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StepUp.FakeAuthority;

namespace StepUp.Api.Tests;

public sealed class FakeAuthorityTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private string _root = "";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _app = FakeAuthorityHost.Build(["--urls", "http://127.0.0.1:0"]);
        await _app.StartAsync(Ct);
        _root = _app.Urls.First();
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_root) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return (verifier, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private async Task<string> CodeAsync(string challenge, string extra = "")
    {
        var url = $"/authorize?response_type=code&client_id=spa&redirect_uri={Uri.EscapeDataString("http://localhost:5173/cb")}&code_challenge={challenge}&code_challenge_method=S256&state=xyz&login_hint=alice{extra}";
        var response = await _http.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("xyz", query["state"]);
        return query["code"]!;
    }

    private Task<HttpResponseMessage> TokenAsync(string code, string verifier) =>
        _http.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = "http://localhost:5173/cb",
            ["client_id"] = "spa",
        }), Ct);

    private static async Task<JsonWebToken> ParseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return new JsonWebToken(body.GetProperty("access_token").GetString());
    }

    [Fact]
    public async Task Pkce_FlowIssuesATokenWithoutMfaByDefault()
    {
        var (verifier, challenge) = Pkce();

        var response = await TokenAsync(await CodeAsync(challenge), verifier);
        var jwt = await ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["pwd"], jwt.GetPayloadValue<string[]>("amr"));
        Assert.Equal(FakeDirectory.Users["alice"].Oid, jwt.GetPayloadValue<string>("oid"));
        Assert.Equal("api://voice-step-up", jwt.Audiences.Single());
    }

    [Fact]
    public async Task Authorize_HonoursAClaimsRequestForNgcmfa()
    {
        var (verifier, challenge) = Pkce();
        var claims = Uri.EscapeDataString("""{"access_token":{"amr":{"essential":true,"values":["ngcmfa"]}}}""");

        var jwt = await ParseAsync(await TokenAsync(await CodeAsync(challenge, $"&claims={claims}"), verifier));

        Assert.Contains("ngcmfa", jwt.GetPayloadValue<string[]>("amr"));
        Assert.Contains("mfa", jwt.GetPayloadValue<string[]>("amr"));
        Assert.True(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - jwt.GetPayloadValue<long>("auth_time")) < 60);
    }

    [Fact]
    public async Task Token_RejectsAWrongVerifierAndCodeReuse()
    {
        var (verifier, challenge) = Pkce();
        var code = await CodeAsync(challenge);

        var wrong = await TokenAsync(code, Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48)));
        var retryWithRight = await TokenAsync(code, verifier);

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, retryWithRight.StatusCode);
    }

    [Fact]
    public async Task Authorize_RequiresS256Pkce()
    {
        var plain = await _http.GetAsync($"/authorize?response_type=code&client_id=spa&redirect_uri={Uri.EscapeDataString("http://localhost:5173/cb")}&code_challenge=abc&code_challenge_method=plain", Ct);
        var none = await _http.GetAsync($"/authorize?response_type=code&client_id=spa&redirect_uri={Uri.EscapeDataString("http://localhost:5173/cb")}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }

    [Fact]
    public async Task Discovery_AndJwks_ValidateTheIssuedToken()
    {
        var (verifier, challenge) = Pkce();
        var jwt = await ParseAsync(await TokenAsync(await CodeAsync(challenge), verifier));
        var discovery = await _http.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", Ct);
        var keys = new JsonWebKeySet(await _http.GetStringAsync("/jwks", Ct));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(jwt.EncodedToken, new TokenValidationParameters
        {
            ValidIssuer = discovery.GetProperty("issuer").GetString(),
            ValidAudience = "api://voice-step-up",
            IssuerSigningKeys = keys.GetSigningKeys(),
        });

        Assert.True(result.IsValid);
        Assert.Equal(["S256"], discovery.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Token_SupportsCorsForTheSpaOriginOnly()
    {
        HttpRequestMessage Preflight(string origin) => new(HttpMethod.Options, "/token") { Headers = { { "Origin", origin }, { "Access-Control-Request-Method", "POST" } } };

        var allowed = await _http.SendAsync(Preflight("http://localhost:5173"), Ct);
        var evil = await _http.SendAsync(Preflight("https://evil.example"), Ct);

        Assert.Equal("http://localhost:5173", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(evil.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
