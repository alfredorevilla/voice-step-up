using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StepUp.Core;

namespace StepUp.Core.Tests;

public class AssertionTests
{
    private sealed class TestKeys : IAssertionKeyProvider
    {
        public TestKeys(string kid) => SigningKey = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };

        public SecurityKey SigningKey { get; }

        public IReadOnlyCollection<SecurityKey> ValidationKeys => [SigningKey];
    }

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly TestKeys _keys = new("kid-1");
    private readonly AssertionOptions _options = new();
    private readonly AssertionBinding _binding = new("oid-1", "tid-1", "payout", null);

    private StepUpAssertionService Service => new(_keys, new InMemoryUsedAssertionStore(), _options, _time);

    private (StepUpAssertionService Service, string Token) IssueToken(AssertionBinding? binding = null)
    {
        var service = Service;
        return (service, service.Issue(binding ?? _binding).Token);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Assertion_AcceptsValidTokenForTheBoundUser()
    {
        var (service, token) = IssueToken();

        Assert.True((await service.ValidateAndConsumeAsync(token, _binding, Ct)).Ok);
    }

    [Fact]
    public void Assertion_CarriesVbmTypAndAudience()
    {
        var jwt = new JsonWebToken(Service.Issue(_binding).Token);

        Assert.Equal("stepup+jwt", jwt.Typ);
        Assert.Equal("ES256", jwt.Alg);
        Assert.Equal("kid-1", jwt.Kid);
        Assert.Contains("vbm", jwt.GetPayloadValue<string[]>("amr"));
        Assert.Equal(_options.Audience, jwt.Audiences.Single());
    }

    [Fact]
    public async Task Assertion_RejectsTamperedToken()
    {
        var (service, token) = IssueToken();
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1][..^2]}AA.{parts[2]}";

        Assert.False((await service.ValidateAndConsumeAsync(tampered, _binding, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsExpiredToken()
    {
        var (service, token) = IssueToken();

        _time.Advance(_options.Lifetime + TimeSpan.FromSeconds(1));

        Assert.False((await service.ValidateAndConsumeAsync(token, _binding, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsWrongUser()
    {
        var (service, token) = IssueToken();

        Assert.False((await service.ValidateAndConsumeAsync(token, _binding with { Oid = "oid-2" }, Ct)).Ok);
        Assert.False((await service.ValidateAndConsumeAsync(token, _binding with { Tid = "tid-2" }, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsWrongAction()
    {
        var (service, token) = IssueToken();

        var result = await service.ValidateAndConsumeAsync(token, _binding with { Action = "delete-account" }, Ct);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Assertion_RejectsChangedPayload()
    {
        var bound = _binding with { PayloadHash = StepUpAssertionService.PayloadHash("100", "alice") };
        var (service, token) = IssueToken(bound);

        var changed = await service.ValidateAndConsumeAsync(token, bound with { PayloadHash = StepUpAssertionService.PayloadHash("9000", "mallory") }, Ct);

        Assert.False(changed.Ok);
    }

    [Fact]
    public async Task Assertion_IsSingleUse()
    {
        var (service, token) = IssueToken();

        var first = await service.ValidateAndConsumeAsync(token, _binding, Ct);
        var second = await service.ValidateAndConsumeAsync(token, _binding, Ct);

        Assert.True(first.Ok);
        Assert.False(second.Ok);
    }

    [Fact]
    public async Task Assertion_RejectsAlgNone()
    {
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"stepup+jwt"}""");
        var payload = Base64UrlEncoder.Encode($$"""{"iss":"{{_options.Issuer}}","aud":"{{_options.Audience}}","sub":"oid-1","tid":"tid-1","act":"payout","jti":"x","exp":{{_time.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds()}}}""");

        Assert.False((await Service.ValidateAndConsumeAsync($"{header}.{payload}.", _binding, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsHs256()
    {
        var symmetric = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "kid-1" };
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            TokenType = StepUpAssertionService.TokenType,
            Expires = _time.GetUtcNow().AddMinutes(5).UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = "oid-1", ["tid"] = "tid-1", ["act"] = "payout", ["jti"] = "j1" },
            SigningCredentials = new SigningCredentials(symmetric, SecurityAlgorithms.HmacSha256),
        });

        Assert.False((await Service.ValidateAndConsumeAsync(token, _binding, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsUnknownKid()
    {
        var stranger = new TestKeys("kid-unknown");
        var token = new StepUpAssertionService(stranger, new InMemoryUsedAssertionStore(), _options, _time).Issue(_binding).Token;

        Assert.False((await Service.ValidateAndConsumeAsync(token, _binding, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsEntraToken()
    {
        var entraLike = new TestKeys("entra-kid");
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://login.microsoftonline.com/tid-1/v2.0",
            Audience = "api://app-id",
            TokenType = "JWT",
            Expires = _time.GetUtcNow().AddMinutes(30).UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = "oid-1", ["oid"] = "oid-1", ["tid"] = "tid-1", ["amr"] = new[] { "vbm" }, ["act"] = "payout", ["jti"] = "j2" },
            SigningCredentials = new SigningCredentials(entraLike.SigningKey, SecurityAlgorithms.EcdsaSha256),
        });

        Assert.False((await Service.ValidateAndConsumeAsync(token, _binding, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsWrongAudienceOrType()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = "api://app-id",
            TokenType = StepUpAssertionService.TokenType,
            Expires = _time.GetUtcNow().AddMinutes(5).UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = "oid-1", ["tid"] = "tid-1", ["act"] = "payout", ["jti"] = "j3" },
            SigningCredentials = new SigningCredentials(_keys.SigningKey, SecurityAlgorithms.EcdsaSha256),
        };
        var wrongAudience = new JsonWebTokenHandler().CreateToken(descriptor);
        descriptor.Audience = _options.Audience;
        descriptor.TokenType = "JWT";
        descriptor.Claims["jti"] = "j4";
        var wrongType = new JsonWebTokenHandler().CreateToken(descriptor);

        Assert.False((await Service.ValidateAndConsumeAsync(wrongAudience, _binding, Ct)).Ok);
        Assert.False((await Service.ValidateAndConsumeAsync(wrongType, _binding, Ct)).Ok);
    }
}
