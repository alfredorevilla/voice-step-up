using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using StepUp.Core;

namespace StepUp.Infrastructure.Verification;

/// <summary>Body of <c>POST v1/analyze</c>: a quality reject, or the probe embedding and transcript. Never a score.</summary>
public sealed record AnalyzeResponse(string ModelId, string? Reject, float[]? Embedding, string? Transcript);

public sealed record HttpVerifierOptions(string ModelId, string? ApiKey = null);

/// <summary>
/// Calls a verifier that runs in another process (story 3: swap the verifier behind the interface). Wire format:
/// <c>POST v1/analyze</c>, body = the WAV bytes, header <c>X-Expected-Phrase</c>, header <c>X-Verifier-Key</c> when a key is set.
/// Any transport error, non-success status or malformed answer throws, which the Api treats as a verifier error and
/// answers with the fallback. Use it on loopback or the compose network only: audio is biometric data and travels unencrypted.
/// </summary>
public sealed partial class HttpVerifier(HttpClient http, HttpVerifierOptions options) : IVerifier
{
    public const string PhraseHeader = "X-Expected-Phrase";
    public const string KeyHeader = "X-Verifier-Key";

    private static readonly HashSet<ReasonCode> QualityRejects = [ReasonCode.Muted, ReasonCode.TooShort, ReasonCode.Noisy];

    /// <summary>Most bytes read from an answer. A real answer is a few kilobytes.</summary>
    public const int MaxResponseBytes = 256 * 1024;

    public const int MaxEmbeddingLength = 4096;

    public const int MaxTranscriptLength = 1000;

    /// <summary>
    /// A client for the remote verifier: no redirects (a redirect would send the audio to another host), no proxy,
    /// no cookies, a size cap on the answer and a timeout.
    /// </summary>
    public static HttpClient CreateClient(Uri baseAddress, TimeSpan timeout) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false })
        {
            BaseAddress = baseAddress,
            Timeout = timeout,
            MaxResponseContentBufferSize = MaxResponseBytes,
        };

    public string ModelId => options.ModelId;

    public async Task<VerifierResult> AnalyzeAsync(ReadOnlyMemory<byte> wav, string expectedPhrase, CancellationToken cancellationToken)
    {
        if (!PhraseShape().IsMatch(expectedPhrase))
        {
            throw new ArgumentException("The expected phrase must be lowercase words separated by spaces.", nameof(expectedPhrase));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/analyze") { Content = new ReadOnlyMemoryContent(wav) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Add(PhraseHeader, expectedPhrase);
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            request.Headers.Add(KeyHeader, options.ApiKey);
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AnalyzeResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The verifier returned an empty answer.");

        if (!string.Equals(body.ModelId, options.ModelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The verifier runs a different model than the one configured.");
        }

        if (body.Reject is not null)
        {
            if (!Enum.TryParse<ReasonCode>(body.Reject, ignoreCase: false, out var reason) || !QualityRejects.Contains(reason))
            {
                throw new InvalidOperationException("The verifier returned an unknown reject.");
            }

            return new VerifierResult(reason, null, null);
        }

        if (body.Embedding is not { Length: > 0 and <= MaxEmbeddingLength } embedding
            || embedding.Any(x => !float.IsFinite(x))
            || body.Transcript is not { Length: <= MaxTranscriptLength })
        {
            throw new InvalidOperationException("The verifier answer has no usable embedding and transcript.");
        }

        return new VerifierResult(null, embedding, body.Transcript);
    }

    [GeneratedRegex("^[a-z]+( [a-z]+)*$")]
    private static partial Regex PhraseShape();
}
