using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StepUp.Core;

public interface IAssertionKeyProvider
{
    /// <summary>The ES256 key that signs assertions. Must carry a <c>KeyId</c>.</summary>
    SecurityKey SigningKey { get; }

    /// <summary>The only keys the validator trusts: the service's own, never an Entra key set.</summary>
    IReadOnlyCollection<SecurityKey> ValidationKeys { get; }
}

public sealed record AssertionOptions
{
    public string Issuer { get; init; } = "voice-step-up";

    /// <summary>The step-up audience. It is never the Entra App ID URI.</summary>
    public string Audience { get; init; } = "voice-step-up/stepup";

    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed record IssuedAssertion(string Token, DateTimeOffset ExpiresAt);

/// <summary>What a presented assertion must be bound to: this user, this action and, if set, this payload.</summary>
public sealed record AssertionBinding(string Oid, string Tid, string Action, string? PayloadHash);

public sealed record AssertionResult(bool Ok, string? Failure, ReasonCode? Code = null);

/// <summary>ADR-0005: ES256, header typ stepup+jwt, own audience, single use, bound to oid, tid and action.</summary>
public sealed class StepUpAssertionService(
    IAssertionKeyProvider keys,
    IUsedAssertionStore used,
    AssertionOptions options,
    TimeProvider time)
{
    public const string TokenType = "stepup+jwt";

    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public static string PayloadHash(string amount, string to) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{amount}\n{to}"))).ToLowerInvariant();

    public IssuedAssertion Issue(AssertionBinding binding)
    {
        var now = time.GetUtcNow();
        var expires = now + options.Lifetime;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = binding.Oid,
            ["tid"] = binding.Tid,
            ["amr"] = new[] { "vbm" },
            ["act"] = binding.Action,
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (binding.PayloadHash is not null)
        {
            claims["ph"] = binding.PayloadHash;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            TokenType = TokenType,
            Claims = claims,
            SigningCredentials = new SigningCredentials(keys.SigningKey, SecurityAlgorithms.EcdsaSha256),
        };
        return new IssuedAssertion(_handler.CreateToken(descriptor), expires);
    }

    /// <summary>Validates and, on success, consumes the jti. A second call with the same token fails.</summary>
    public async Task<AssertionResult> ValidateAndConsumeAsync(string token, AssertionBinding binding, CancellationToken cancellationToken)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys.ValidationKeys,
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = [TokenType],
            RequireExpirationTime = true,
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, _) =>
                notBefore is { } nbf && expires is { } exp && nbf <= time.GetUtcNow().UtcDateTime && time.GetUtcNow().UtcDateTime < exp,
            ClockSkew = TimeSpan.Zero,
        };

        var result = await _handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
        {
            return new AssertionResult(false, "invalid token", ReasonCode.AssertionInvalid);
        }

        string? Claim(string name) => result.Claims.TryGetValue(name, out var v) ? v?.ToString() : null;

        if (Claim("sub") != binding.Oid || Claim("tid") != binding.Tid)
        {
            return new AssertionResult(false, "wrong user", ReasonCode.AssertionInvalid);
        }

        if (Claim("act") != binding.Action)
        {
            return new AssertionResult(false, "wrong action", ReasonCode.AssertionInvalid);
        }

        if (Claim("ph") != binding.PayloadHash)
        {
            return new AssertionResult(false, "payload changed", ReasonCode.AssertionInvalid);
        }

        var jti = Claim("jti");
        if (string.IsNullOrEmpty(jti))
        {
            return new AssertionResult(false, "no jti", ReasonCode.AssertionInvalid);
        }

        var expiresAt = result.SecurityToken is JsonWebToken jwt ? new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero) : time.GetUtcNow() + options.Lifetime;
        return await used.TryUseAsync(jti, expiresAt, cancellationToken)
            ? new AssertionResult(true, null)
            : new AssertionResult(false, "replayed", ReasonCode.AssertionReplayed);
    }
}
