using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Identity.Web;

namespace StepUp.Api.Auth;

public static class AuthSetup
{
    public const string UserPolicy = "user";
    public const string AnalystPolicy = "analyst";

    private static bool IsAllowedUser(System.Security.Claims.ClaimsPrincipal user, string? expectedTid) =>
        CallerFactory.IsDelegatedUserToken(user)
        && (expectedTid is null || string.Equals(user.FindFirst("tid")?.Value, expectedTid, StringComparison.OrdinalIgnoreCase));

    public static void AddStepUpAuth(this WebApplicationBuilder builder)
    {
        var mode = builder.Configuration["Auth:Mode"] ?? "Entra";
        // Single tenant: in Entra mode the token's tid must be the configured one, on top of Identity.Web's issuer validation.
        var expectedTid = mode == "Entra" ? builder.Configuration["AzureAd:TenantId"] : null;
        var authentication = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        switch (mode)
        {
            case "Entra":
                // Single tenant (AzureAd:TenantId is a GUID, never "common"), Audience = the App ID URI.
                // The fake issuer is never configured here, so Entra mode cannot trust it.
                authentication.AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
                break;
            case "Fake":
                FakeModeGuard.Require("Auth:Mode=Fake", builder.Configuration.GetValue<bool>("Demo:Enabled"), builder.Environment.EnvironmentName);
                var fake = builder.Configuration.GetSection("Auth:Fake");
                authentication.AddJwtBearer(options =>
                {
                    options.MetadataAddress = fake["MetadataAddress"] ?? "";
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = fake["Issuer"],
                        ValidateAudience = true,
                        ValidAudience = fake["Audience"],
                        ValidateIssuerSigningKey = true,
                        RequireSignedTokens = true,
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                });
                break;
            default:
                throw new InvalidOperationException($"Auth:Mode must be Entra or Fake, not '{mode}'.");
        }

        // The same claim handling in both modes: short claim names (amr, oid, tid, auth_time), roles from "roles".
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters.RoleClaimType = "roles";
            options.TokenValidationParameters.NameClaimType = "name";
        });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(UserPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c => IsAllowedUser(c.User, expectedTid)))
            .AddPolicy(AnalystPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(c => IsAllowedUser(c.User, expectedTid))
                .RequireRole(CallerFactory.AnalystRole));
    }
}
