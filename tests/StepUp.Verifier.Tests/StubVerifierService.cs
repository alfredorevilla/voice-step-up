using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StepUp.Core;
using StepUp.Infrastructure.Verification;

namespace StepUp.Verifier.Tests;

/// <summary>
/// A verifier service on a loopback port that wraps the fake verifier: the reference for the HttpVerifier wire format.
/// A real remote verifier implements the same route.
/// </summary>
internal sealed class StubVerifierService : IAsyncDisposable
{
    public const string Key = "stub-key";

    private readonly WebApplication app;

    private StubVerifierService(WebApplication app, Uri address)
    {
        this.app = app;
        Address = address;
    }

    public Uri Address { get; }

    public int Calls { get; private set; }

    public string? LastKeyHeader { get; private set; }

    public Func<HttpContext, IResult?>? Override { get; set; }

    public static async Task<StubVerifierService> StartAsync(string modelId = "fake-verifier-v1", bool requireKey = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var verifier = new FakeVerifier();
        StubVerifierService? self = null;

        app.MapPost("/v1/analyze", async (HttpContext http) =>
        {
            self!.Calls++;
            self.LastKeyHeader = http.Request.Headers[HttpVerifier.KeyHeader];
            if (self.Override?.Invoke(http) is { } custom)
            {
                return custom;
            }

            if (requireKey && self.LastKeyHeader != Key)
            {
                return Results.Unauthorized();
            }

            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, http.RequestAborted);
            var phrase = http.Request.Headers[HttpVerifier.PhraseHeader].ToString();
            var result = await verifier.AnalyzeAsync(buffer.ToArray(), phrase, http.RequestAborted);
            return Results.Json(new AnalyzeResponse(modelId, result.Reject?.ToString(), result.Embedding, result.Transcript));
        });

        await app.StartAsync();
        var address = new Uri(app.Urls.First());
        self = new StubVerifierService(app, address);
        return self;
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
