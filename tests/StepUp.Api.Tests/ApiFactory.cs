using Microsoft.AspNetCore.TestHost;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using StepUp.Core;
using StepUp.FakeAuthority;
using StepUp.Infrastructure.Verification;

namespace StepUp.Api.Tests;

/// <summary>Captures every log line so a test can prove that no audio, score or token is logged (TM-009).</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<string> Lines { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Capture(this);

    public void Dispose()
    {
    }

    private sealed class Capture(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.Lines)
            {
                owner.Lines.Add(formatter(state, exception) + (exception is null ? "" : "\n" + exception));
            }
        }
    }
}

/// <summary>The real API in Fake auth mode with a fake clock, a temp database and the fake verifier (or the one a test supplies).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Audience = "api://voice-step-up";
    public const string Issuer = "https://fake-authority.test/v2.0";
    public const string SpaOrigin = "http://localhost:5173";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "stepup-api-" + Guid.NewGuid().ToString("N"));
    private readonly IVerifier? _verifier;
    private readonly Dictionary<string, string?> _extra;

    public ApiFactory(IVerifier? verifier = null, Dictionary<string, string?>? config = null)
    {
        _verifier = verifier;
        _extra = config ?? [];
        Time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Minter = new FakeTokenMinter(Issuer, Audience, Time);
    }

    /// <summary>The temp directory that holds this instance's database and key files.</summary>
    public string DataRoot => _root;

    public FakeTimeProvider Time { get; }

    public FakeTokenMinter Minter { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        {
            var values = new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true",
                ["Auth:Mode"] = "Fake",
                ["Auth:Fake:Issuer"] = Issuer,
                ["Auth:Fake:Audience"] = Audience,
                ["Verifier:Mode"] = "Fake",
                ["Storage:DatabasePath"] = Path.Combine(_root, "stepup.db"),
                ["Storage:KeyDirectory"] = Path.Combine(_root, "keys"),
                ["Cors:AllowedOrigins:0"] = SpaOrigin,
                ["StepUp:ThresholdSeed"] = "0.8",
                ["StepUp:ChallengePerMinute"] = "1000",
                ["StepUp:VerifyPerMinute"] = "1000",
                ["StepUp:SamplePerMinute"] = "1000",
                ["StepUp:GeneralPerMinute"] = "100000",
            };
            foreach (var (key, value) in _extra)
            {
                values[key] = value;
            }

            builder.UseSettings(values);
        }

        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            if (_verifier is not null)
            {
                services.AddSingleton(_verifier);
            }

            // The in-process fake authority: validate against the minter's key, no discovery over the network.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(Minter.SigningKey);
                o.Configuration = configuration;
            });
        });
    }

    public HttpClient ClientFor(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public string TokenFor(FakeUser user, string[]? amr = null, TimeSpan? authAge = null, bool noAuthTime = false, string[]? roles = null) =>
        Minter.Mint(new MintRequest(
            user.Oid,
            FakeDirectory.FakeTid,
            amr ?? ["pwd"],
            noAuthTime ? null : Time.GetUtcNow() - (authAge ?? TimeSpan.Zero),
            roles ?? user.Roles,
            Name: user.Name));

    public string FreshMfa(FakeUser user) => TokenFor(user, ["pwd", "mfa"]);

    public static byte[] Speech(byte speaker, string phrase, FakeQuality quality = FakeQuality.Good) => FakeAudio.Create(speaker, phrase, quality);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
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
}

public static class Users
{
    public static FakeUser Alice => FakeDirectory.Users["alice"];

    public static FakeUser Bob => FakeDirectory.Users["bob"];

    public static FakeUser Analyst => FakeDirectory.Users["analyst"];
}

public static class HttpExtensions
{
    /// <summary>UseSetting is applied before Program.cs reads configuration; AddInMemoryCollection is applied too late for that.</summary>
    public static IWebHostBuilder UseSettings(this IWebHostBuilder builder, IEnumerable<KeyValuePair<string, string?>> values)
    {
        foreach (var (key, value) in values)
        {
            builder.UseSetting(key, value);
        }

        return builder;
    }

    public static async Task<string?> CodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public static HttpContent Wav(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return content;
    }
}
