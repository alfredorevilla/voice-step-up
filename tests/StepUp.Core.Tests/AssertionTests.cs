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

        var result = await service.ValidateAndConsumeAsync(tampered, _binding, Ct);

        Assert.False(result.Ok);
        Assert.Equal("invalid token", result.Failure);
    }

    [Fact]
    public async Task Assertion_RejectsExpiredToken()
    {
        var (service, token) = IssueToken();

        _time.Advance(_options.Lifetime + TimeSpan.FromSeconds(1));
        var result = await service.ValidateAndConsumeAsync(token, _binding, Ct);

        Assert.False(result.Ok);
        Assert.Equal("invalid token", result.Failure);
    }

    [Fact]
    public async Task Assertion_RejectsWrongUser()
    {
        var (service, token) = IssueToken();

        var otherUser = await service.ValidateAndConsumeAsync(token, _binding with { Oid = "oid-2" }, Ct);
        var otherTenant = await service.ValidateAndConsumeAsync(token, _binding with { Tid = "tid-2" }, Ct);

        Assert.Equal("wrong user", otherUser.Failure);
        Assert.Equal("wrong user", otherTenant.Failure);
    }

    [Fact]
    public async Task Assertion_RejectsWrongAction()
    {
        var (service, token) = IssueToken();

        var result = await service.ValidateAndConsumeAsync(token, _binding with { Action = "delete-account" }, Ct);

        Assert.False(result.Ok);
        Assert.Equal("wrong action", result.Failure);
    }

    [Fact]
    public async Task Assertion_RejectsChangedPayload()
    {
        var bound = _binding with { PayloadHash = StepUpAssertionService.PayloadHash("100", "alice") };
        var (service, token) = IssueToken(bound);

        var changed = await service.ValidateAndConsumeAsync(token, bound with { PayloadHash = StepUpAssertionService.PayloadHash("9000", "mallory") }, Ct);

        Assert.False(changed.Ok);
        Assert.Equal("payload changed", changed.Failure);
    }

    [Fact]
    public async Task Assertion_IsSingleUse()
    {
        var (service, token) = IssueToken();

        var first = await service.ValidateAndConsumeAsync(token, _binding, Ct);
        var second = await service.ValidateAndConsumeAsync(token, _binding, Ct);

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        Assert.Equal("replayed", second.Failure);
        Assert.Equal(ReasonCode.AssertionReplayed, second.Code);
    }

    [Fact]
    public async Task Assertion_RejectsAlgNone()
    {
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"stepup+jwt"}""");
        var payload = Base64UrlEncoder.Encode($$"""{"iss":"{{_options.Issuer}}","aud":"{{_options.Audience}}","sub":"oid-1","tid":"tid-1","act":"payout","jti":"x","exp":{{_time.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds()}}}""");

        var result = await Service.ValidateAndConsumeAsync($"{header}.{payload}.", _binding, Ct);

        Assert.False(result.Ok);
        Assert.Equal("invalid token", result.Failure);
    }

    private sealed class Keys(SecurityKey signing, params SecurityKey[] validation) : IAssertionKeyProvider
    {
        public SecurityKey SigningKey { get; } = signing;

        public IReadOnlyCollection<SecurityKey> ValidationKeys { get; } = validation;
    }

    private static readonly string[] ValidAmr = ["vbm"];

    /// <summary>A token that is valid in every respect except what <paramref name="tweak"/> changes, so a rejection has one cause.</summary>
    private string Token(SecurityKey key, string algorithm, Action<SecurityTokenDescriptor>? tweak = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            TokenType = StepUpAssertionService.TokenType,
            // Pin the other lifetime claims to the fake clock too: CreateToken would otherwise stamp nbf/iat with the real clock.
            NotBefore = _time.GetUtcNow().UtcDateTime,
            IssuedAt = _time.GetUtcNow().UtcDateTime,
            Expires = _time.GetUtcNow().AddMinutes(5).UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = "oid-1", ["tid"] = "tid-1", ["amr"] = ValidAmr, ["act"] = "payout", ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = new SigningCredentials(key, algorithm),
        };
        tweak?.Invoke(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private ECDsa TrustedEcdsa => ((ECDsaSecurityKey)_keys.SigningKey).ECDsa;

    [Fact]
    public async Task Assertion_RejectsHs256()
    {
        // The HMAC key is in the trusted key set under its own kid, so only the pinned algorithm can reject this token.
        var symmetric = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "kid-hs" };
        var service = new StepUpAssertionService(new Keys(_keys.SigningKey, _keys.SigningKey, symmetric), new InMemoryUsedAssertionStore(), _options, _time);
        var hs256 = Token(symmetric, SecurityAlgorithms.HmacSha256);
        var control = Token(_keys.SigningKey, SecurityAlgorithms.EcdsaSha256);

        Assert.True((await service.ValidateAndConsumeAsync(control, _binding, Ct)).Ok);
        Assert.False((await service.ValidateAndConsumeAsync(hs256, _binding, Ct)).Ok);
    }

    [Fact]
    public async Task Assertion_RejectsUnknownKid()
    {
        // Same private key, so the signature is valid; only the unknown kid can reject it.
        var sameKeyOtherKid = new ECDsaSecurityKey(TrustedEcdsa) { KeyId = "kid-unknown" };
        var rightKid = new ECDsaSecurityKey(TrustedEcdsa) { KeyId = "kid-1" };

        Assert.True((await Service.ValidateAndConsumeAsync(Token(rightKid, SecurityAlgorithms.EcdsaSha256), _binding, Ct)).Ok);
        Assert.False((await Service.ValidateAndConsumeAsync(Token(sameKeyOtherKid, SecurityAlgorithms.EcdsaSha256), _binding, Ct)).Ok);
    }

    [Theory]
    [InlineData("typ")]
    [InlineData("aud")]
    [InlineData("iss")]
    [InlineData("all")]
    public async Task Assertion_RejectsEntraToken(string whatLooksLikeEntra)
    {
        // Signed by the trusted key with a valid signature and lifetime: only the typ, aud or iss check can reject it.
        var token = Token(_keys.SigningKey, SecurityAlgorithms.EcdsaSha256, d =>
        {
            if (whatLooksLikeEntra is "typ" or "all")
            {
                d.TokenType = "JWT";
            }

            if (whatLooksLikeEntra is "aud" or "all")
            {
                d.Audience = "api://app-id";
            }

            if (whatLooksLikeEntra is "iss" or "all")
            {
                d.Issuer = "https://login.microsoftonline.com/tid-1/v2.0";
            }
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
            // Pin the other lifetime claims to the fake clock too: CreateToken would otherwise stamp nbf/iat with the real clock.
            NotBefore = _time.GetUtcNow().UtcDateTime,
            IssuedAt = _time.GetUtcNow().UtcDateTime,
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
