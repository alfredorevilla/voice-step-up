using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StepUp.FakeAuthority;

public sealed record MintRequest(
    string Oid,
    string Tid,
    string[] Amr,
    DateTimeOffset? AuthTime,
    string[]? Roles = null,
    string? Scope = "access_as_user",
    string? IdTyp = null,
    TimeSpan? Lifetime = null,
    string Name = "Test User");

/// <summary>
/// Signs Entra-shaped v2.0 access tokens with a key generated per run. Used by tests and the offline demo.
/// It is test and demo infrastructure only, never a production component (ADR-0007).
/// </summary>
public sealed class FakeTokenMinter
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TimeProvider _time;

    public FakeTokenMinter(string issuer, string audience, TimeProvider? time = null)
    {
        Issuer = issuer;
        Audience = audience;
        _time = time ?? TimeProvider.System;
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "fake-" + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(6)) };
    }

    public string Issuer { get; }

    public string Audience { get; }

    public RsaSecurityKey SigningKey { get; }

    public JsonWebKeySet Jwks
    {
        get
        {
            var set = new JsonWebKeySet();
            set.Keys.Add(JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = SigningKey.KeyId }));
            return set;
        }
    }

    public string Mint(MintRequest request)
    {
        var now = _time.GetUtcNow();
        var claims = new Dictionary<string, object>
        {
            ["oid"] = request.Oid,
            ["sub"] = request.Oid,
            ["tid"] = request.Tid,
            ["name"] = request.Name,
            ["ver"] = "2.0",
            ["amr"] = request.Amr,
        };
        if (request.AuthTime is { } authTime)
        {
            claims["auth_time"] = authTime.ToUnixTimeSeconds();
        }

        if (request.Roles is { Length: > 0 })
        {
            claims["roles"] = request.Roles;
        }

        if (request.Scope is not null)
        {
            claims["scp"] = request.Scope;
        }

        if (request.IdTyp is not null)
        {
            claims["idtyp"] = request.IdTyp;
        }

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + (request.Lifetime ?? TimeSpan.FromHours(1))).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
