using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace StepUp.FakeAuthority;

/// <summary>
/// An offline OIDC authority for CI and the one-command demo: discovery, JWKS, authorize (auto sign-in) and token
/// with PKCE (S256 only) and CORS on the token endpoint. A <c>claims</c> parameter asking for <c>ngcmfa</c> yields an
/// <c>amr</c> with <c>mfa</c> and <c>ngcmfa</c> and a fresh <c>auth_time</c>, which is how the demo shows the fallback.
/// </summary>
public static class FakeAuthorityHost
{
    private sealed record PendingCode(string UserSlug, string Challenge, string RedirectUri, string ClientId, DateTimeOffset Expires, bool Mfa, DateTimeOffset AuthTime);

    private static readonly string[] CodeOnly = ["code"];
    private static readonly string[] S256Only = ["S256"];
    private static readonly string[] Rs256Only = ["RS256"];
    private static readonly string[] PairwiseOnly = ["pairwise"];

    /// <summary>At most this many distinct issuers (Host headers) get a signing key; more are refused.</summary>
    private const int MaxIssuers = 8;

    public static void Main(string[] args)
    {
        var app = Build(args);

        // Standalone, the authority mints tokens for any identity: it runs only with Demo:Enabled in Development or Demo (threat model TM-013).
        // Build stays unguarded so tests can host it in-process.
        if (!app.Configuration.GetValue<bool>("Demo:Enabled")
            || (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Demo")))
        {
            throw new InvalidOperationException(
                $"The fake authority is fake and needs Demo:Enabled=true in the Development or Demo environment (environment is '{app.Environment.EnvironmentName}').");
        }

        app.Run();
    }

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        var audience = builder.Configuration["FakeAuthority:Audience"] ?? "api://voice-step-up";
        var origins = (builder.Configuration["FakeAuthority:AllowedOrigins"] ?? "http://localhost:5173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var codes = new ConcurrentDictionary<string, PendingCode>();
        var minters = new ConcurrentDictionary<string, FakeTokenMinter>();
        var time = TimeProvider.System;

        // Behind a container network the API reaches the authority by a service name while the browser uses localhost;
        // PublicUrl pins the issuer (and so the signing key) to the address the browser sees.
        var publicUrl = builder.Configuration["FakeAuthority:PublicUrl"]?.TrimEnd('/');

        FakeTokenMinter MinterFor(HttpRequest r)
        {
            var issuer = $"{publicUrl ?? $"{r.Scheme}://{r.Host}"}/v2.0";
            if (!minters.TryGetValue(issuer, out var existing) && minters.Count >= MaxIssuers)
            {
                throw new BadHttpRequestException("too many issuers");
            }

            return existing ?? minters.GetOrAdd(issuer, i => new FakeTokenMinter(i, audience, time));
        }

        app.Use(async (ctx, next) =>
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && origins.Contains(origin, StringComparer.Ordinal))
            {
                ctx.Response.Headers.AccessControlAllowOrigin = origin;
                ctx.Response.Headers.AccessControlAllowHeaders = "content-type";
                ctx.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
                ctx.Response.Headers.Vary = "Origin";
            }

            if (HttpMethods.IsOptions(ctx.Request.Method))
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next(ctx);
        });

        app.MapGet("/.well-known/openid-configuration", (HttpRequest r) =>
        {
            var m = MinterFor(r);
            var root = $"{r.Scheme}://{r.Host}";
            return Results.Json(new
            {
                issuer = m.Issuer,
                authorization_endpoint = $"{root}/authorize",
                token_endpoint = $"{root}/token",
                jwks_uri = $"{root}/jwks",
                response_types_supported = CodeOnly,
                code_challenge_methods_supported = S256Only,
                id_token_signing_alg_values_supported = Rs256Only,
                subject_types_supported = PairwiseOnly,
            });
        });

        app.MapGet("/jwks", (HttpRequest r) => Results.Content(System.Text.Json.JsonSerializer.Serialize(MinterFor(r).Jwks), "application/json"));

        app.MapGet("/authorize", (HttpRequest r) =>
        {
            var q = r.Query;
            if (q["response_type"] != "code" || q["code_challenge_method"] != "S256" || string.IsNullOrEmpty(q["code_challenge"]))
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "code flow with S256 PKCE is required" });
            }

            var redirect = q["redirect_uri"].ToString();
            if (!Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri)
                || !origins.Contains(redirectUri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal))
            {
                // The allow-list is the same one CORS uses (FakeAuthority:AllowedOrigins), so a code is never sent to an arbitrary site.
                return Results.BadRequest(new { error = "invalid_request", error_description = "redirect_uri" });
            }

            var slug = q["login_hint"].ToString();
            if (!FakeDirectory.Users.ContainsKey(slug))
            {
                slug = "alice";
            }

            var wantsMfa = q["claims"].ToString().Contains("ngcmfa", StringComparison.Ordinal);
            var code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(24));
            codes[code] = new PendingCode(slug, q["code_challenge"]!, redirect, q["client_id"].ToString(), time.GetUtcNow().AddSeconds(60), wantsMfa, time.GetUtcNow());
            var separator = redirect.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            return Results.Redirect($"{redirect}{separator}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(q["state"].ToString())}");
        });

        app.MapPost("/token", async (HttpRequest r) =>
        {
            var form = await r.ReadFormAsync();
            if (form["grant_type"] != "authorization_code" || !codes.TryRemove(form["code"].ToString(), out var pending) || pending.Expires < time.GetUtcNow())
            {
                return Results.BadRequest(new { error = "invalid_grant" });
            }

            var verifier = form["code_verifier"].ToString();
            var computed = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            if (verifier.Length < 43 || computed != pending.Challenge || form["redirect_uri"] != pending.RedirectUri)
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "PKCE verification failed" });
            }

            var user = FakeDirectory.Users[pending.UserSlug];
            string[] amr = pending.Mfa ? ["pwd", "mfa", "ngcmfa"] : ["pwd"];
            var token = MinterFor(r).Mint(new MintRequest(user.Oid, FakeDirectory.FakeTid, amr, pending.AuthTime, user.Roles, Name: user.Name));
            return Results.Json(new { token_type = "Bearer", access_token = token, expires_in = 3600, scope = "access_as_user" });
        });

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        return app;
    }
}
