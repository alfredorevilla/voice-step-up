using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StepUp.Api.Auth;
using StepUp.Api.Endpoints;
using StepUp.Api.Flow;
using StepUp.Core;
using StepUp.Infrastructure;
using StepUp.Infrastructure.Persistence;
using StepUp.Infrastructure.Verification;
using StepUp.Verifier.Sherpa;

var builder = WebApplication.CreateBuilder(args);
var demoFlag = builder.Configuration.GetValue<bool>("Demo:Enabled");
var options = builder.Configuration.GetSection("StepUp").Get<StepUpOptions>() ?? new StepUpOptions();
builder.Services.AddSingleton(options);

builder.AddStepUpAuth();

var storage = builder.Configuration.GetSection("Storage");
var keyDirectory = storage["KeyDirectory"] ?? "data/keys";
builder.Services.AddDataProtection()
    .SetApplicationName("voice-step-up")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(keyDirectory, "dataprotection")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddStepUpInfrastructure(storage["DatabasePath"] ?? "data/stepup.db", keyDirectory);

switch (builder.Configuration["Verifier:Mode"] ?? "Sherpa")
{
    case "Fake":
        FakeModeGuard.Require("Verifier:Mode=Fake", demoFlag, builder.Environment.EnvironmentName);
        builder.Services.AddSingleton<IVerifier, FakeVerifier>();
        break;
    case "Sherpa":
        builder.Services.Configure<SherpaVerifierOptions>(builder.Configuration.GetSection("Verifier:Sherpa"));
        builder.Services.AddSingleton<IVerifier, SherpaVerifier>();
        break;
    case "Http":
        var http = builder.Configuration.GetSection("Verifier:Http");
        var endpoint = VerifierEndpointGuard.Require(http["BaseAddress"]);
        var modelId = http["ModelId"];
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("Verifier:Http:ModelId must name the model the remote verifier runs.");
        }

        // The key comes from the environment (Verifier__Http__ApiKey), never from a file in the repo.
        var httpOptions = new HttpVerifierOptions(modelId, http["ApiKey"]);
        builder.Services.AddSingleton<IVerifier>(_ => new HttpVerifier(new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(30) }, httpOptions));
        break;
    default:
        throw new InvalidOperationException("Verifier:Mode must be Sherpa, Http or Fake.");
}

builder.Services.AddSingleton<IChallengeService>(sp => new ChallengeService(sp.GetRequiredService<IChallengeStore>(), options.Challenge, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IFallbackFactor>(sp => new FreshMfaFallback(options.Fallback, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IStepUpPolicy, StepUpPolicy>();
builder.Services.AddSingleton(sp => new StepUpAssertionService(
    sp.GetRequiredService<IAssertionKeyProvider>(), sp.GetRequiredService<IUsedAssertionStore>(), options.Assertion, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<VerifierGate>();
builder.Services.AddSingleton<VerificationService>();

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddPolicy("spa", p => p.WithOrigins(origins).AllowAnyHeader().WithMethods("GET", "POST", "PUT", "DELETE")));

builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (context, token) =>
    {
        await Problems.Create(StatusCodes.Status429TooManyRequests, ReasonCode.RateLimited, fallback: true).ExecuteAsync(context.HttpContext);
    };

    // Per user (the stated attacker holds a valid token); the address is only the key for anonymous callers.
    static string Key(HttpContext context) =>
        context.User.FindFirstValue("oid") ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        Key(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = options.GeneralPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    void Add(string name, int permits) => o.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
        Key(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    Add("sample", options.SamplePerMinute);
    Add("challenge", options.ChallengePerMinute);
    Add("verify", options.VerifyPerMinute);
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storage["DatabasePath"] ?? "data/stepup.db"))!);
    await db.Database.EnsureCreatedAsync();

    // Seed data, not a hardcoded threshold: the store is the source of truth after this.
    var thresholds = scope.ServiceProvider.GetRequiredService<IThresholdStore>();
    var modelId = scope.ServiceProvider.GetRequiredService<IVerifier>().ModelId;
    if (await thresholds.GetActiveAsync(StepUpOptions.ThresholdName, modelId, CancellationToken.None) is null)
    {
        await thresholds.SetAsync(new ThresholdEntry(StepUpOptions.ThresholdName, modelId, options.ThresholdSeed, DateTimeOffset.UtcNow, "seed"), CancellationToken.None);
    }
}

app.UseExceptionHandler();
app.UseCors("spa");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapStepUpApi();

app.Run();

public partial class Program;
