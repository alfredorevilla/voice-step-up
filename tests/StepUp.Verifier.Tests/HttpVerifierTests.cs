using System.Net;
using Microsoft.AspNetCore.Http;
using StepUp.Core;
using StepUp.Infrastructure.Verification;

namespace StepUp.Verifier.Tests;

/// <summary>The HttpVerifier through the shared contract, against a stub verifier service on loopback.</summary>
public class HttpVerifierContractTests : VerifierContract, IAsyncLifetime
{
    private StubVerifierService stub = null!;
    private readonly List<HttpClient> clients = [];

    public async ValueTask InitializeAsync() => stub = await StubVerifierService.StartAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }

        await stub.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected override IVerifier Create()
    {
        var client = new HttpClient { BaseAddress = stub.Address };
        clients.Add(client);
        return new HttpVerifier(client, new HttpVerifierOptions("fake-verifier-v1"));
    }

    protected override byte[] Speech(int speaker, string phrase) => FakeAudio.Create((byte)speaker, phrase);

    protected override byte[] MutedAudio() => FakeAudio.Create(1, Phrase, FakeQuality.Muted);

    protected override byte[] TooShortAudio() => FakeAudio.Create(1, Phrase, FakeQuality.TooShort);
}

public sealed class HttpVerifierBehaviourTests : IAsyncLifetime
{
    private const string Phrase = "apple anchor arrow autumn";
    private static readonly byte[] Wav = FakeAudio.Create(1, Phrase);

    private StubVerifierService stub = null!;
    private HttpClient client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        stub = await StubVerifierService.StartAsync(requireKey: true);
        client = new HttpClient { BaseAddress = stub.Address };
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await stub.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private HttpVerifier With(string modelId = "fake-verifier-v1", string? key = StubVerifierService.Key) =>
        new(client, new HttpVerifierOptions(modelId, key));

    [Fact]
    public async Task Analyze_SendsTheSharedKey()
    {
        var result = await With().AnalyzeAsync(Wav, Phrase, Ct);

        Assert.Null(result.Reject);
        Assert.Equal(StubVerifierService.Key, stub.LastKeyHeader);
    }

    [Fact]
    public async Task Analyze_WithoutTheKey_FailsClosedWithAnException()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => With(key: null).AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Fact]
    public async Task Analyze_WithAWrongKey_FailsClosedWithAnException()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => With(key: "nope").AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Fact]
    public async Task Analyze_WhenTheServiceRunsAnotherModel_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => With(modelId: "some-other-model").AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Analyze_OnAServerError_Throws(HttpStatusCode status)
    {
        stub.Override = _ => Results.StatusCode((int)status);

        await Assert.ThrowsAsync<HttpRequestException>(() => With().AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Fact]
    public async Task Analyze_WhenTheAnswerClaimsAPass_ButHasNoEmbedding_Throws()
    {
        stub.Override = _ => Results.Json(new AnalyzeResponse("fake-verifier-v1", null, null, "apple anchor arrow autumn"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => With().AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Fact]
    public async Task Analyze_WhenTheAnswerHasANonFiniteEmbedding_Throws()
    {
        stub.Override = _ => Results.Content("""{"modelId":"fake-verifier-v1","reject":null,"embedding":[1.0,1e999],"transcript":"x"}""", "application/json");

        await Assert.ThrowsAnyAsync<Exception>(() => With().AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Theory]
    [InlineData("PhraseMismatch")]
    [InlineData("BelowThreshold")]
    [InlineData("NoVoiceprint")]
    [InlineData("nonsense")]
    public async Task Analyze_RejectsOnlyCarryQualityReasonCodes(string reject)
    {
        stub.Override = _ => Results.Json(new AnalyzeResponse("fake-verifier-v1", reject, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => With().AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Theory]
    [InlineData("Muted", ReasonCode.Muted)]
    [InlineData("TooShort", ReasonCode.TooShort)]
    [InlineData("Noisy", ReasonCode.Noisy)]
    public async Task Analyze_MapsQualityRejects(string reject, ReasonCode expected)
    {
        stub.Override = _ => Results.Json(new AnalyzeResponse("fake-verifier-v1", reject, null, null));

        var result = await With().AnalyzeAsync(Wav, Phrase, Ct);

        Assert.Equal(expected, result.Reject);
        Assert.Null(result.Embedding);
    }

    [Theory]
    [InlineData("Apple Anchor")]
    [InlineData("apple\r\nX-Injected: 1")]
    [InlineData("apple  anchor")]
    [InlineData("")]
    [InlineData("apple1")]
    public async Task Analyze_RefusesAPhraseThatIsNotPlainWords_BeforeSendingAnything(string phrase)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => With().AnalyzeAsync(Wav, phrase, Ct));

        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task Analyze_WhenTheServiceIsSlow_HonoursTheCallersTimeout()
    {
        stub.Override = _ => new HoldsTheResponse(TimeSpan.FromSeconds(10));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => With().AnalyzeAsync(Wav, Phrase, cts.Token));
    }

    /// <summary>A response that never completes on its own: it waits asynchronously until the caller gives up.</summary>
    private sealed class HoldsTheResponse(TimeSpan hold) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            try
            {
                await Task.Delay(hold, httpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // The caller gave up, which is what the test wants.
            }
        }
    }

    [Fact]
    public async Task CreateClient_DoesNotFollowARedirect_SoAudioNeverLeavesTheConfiguredHost()
    {
        await using var elsewhere = await StubVerifierService.StartAsync();
        stub.Override = _ => Results.Redirect(new Uri(elsewhere.Address, "v1/analyze").ToString(), permanent: false, preserveMethod: true);
        using var hardened = HttpVerifier.CreateClient(stub.Address, TimeSpan.FromSeconds(5));
        var verifier = new HttpVerifier(hardened, new HttpVerifierOptions("fake-verifier-v1", StubVerifierService.Key));

        await Assert.ThrowsAsync<HttpRequestException>(() => verifier.AnalyzeAsync(Wav, Phrase, Ct));

        Assert.Equal(0, elsewhere.Calls);
    }

    [Fact]
    public async Task CreateClient_RefusesAnOversizedAnswer()
    {
        // A complete, valid answer plus padding: only the size cap can reject it.
        var padding = new string('x', HttpVerifier.MaxResponseBytes * 2);
        var embedding = string.Join(',', Enumerable.Repeat("0.5", 256));
        stub.Override = _ => Results.Content($$"""{"modelId":"fake-verifier-v1","reject":null,"embedding":[{{embedding}}],"transcript":"apple","padding":"{{padding}}"}""", "application/json");
        using var hardened = HttpVerifier.CreateClient(stub.Address, TimeSpan.FromSeconds(5));
        var verifier = new HttpVerifier(hardened, new HttpVerifierOptions("fake-verifier-v1", StubVerifierService.Key));

        await Assert.ThrowsAsync<HttpRequestException>(() => verifier.AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Fact]
    public async Task Analyze_RefusesAnEmbeddingLongerThanAnyRealModelProduces()
    {
        stub.Override = _ => Results.Json(new AnalyzeResponse("fake-verifier-v1", null, new float[HttpVerifier.MaxEmbeddingLength + 1], "x"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => With().AnalyzeAsync(Wav, Phrase, Ct));
    }

    [Fact]
    public async Task Analyze_RefusesATranscriptLongerThanAPhrase()
    {
        stub.Override = _ => Results.Json(new AnalyzeResponse("fake-verifier-v1", null, new float[256], new string('a', HttpVerifier.MaxTranscriptLength + 1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => With().AnalyzeAsync(Wav, Phrase, Ct));
    }
}
