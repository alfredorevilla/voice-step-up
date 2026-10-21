using Microsoft.Extensions.Options;
using SherpaOnnx;
using StepUp.Core;

namespace StepUp.Verifier.Sherpa;

public sealed class SherpaVerifierOptions
{
    /// <summary>Directory that holds the model files and, by default, <c>models.lock</c>.</summary>
    public string ModelsDir { get; set; } = "models";

    public string? LockFile { get; set; }

    /// <summary>Threads each model may use.</summary>
    public int Threads { get; set; } = 2;

    /// <summary>Analyses that may run at once. Others wait for a slot or the caller's cancellation.</summary>
    public int MaxConcurrent { get; set; } = 2;
}

/// <summary>
/// In-process speaker embedding (WeSpeaker) and phrase transcript (Whisper base.en) through sherpa-onnx
/// (ADR-0001, docs/design/verifier.md). Extracts only: scoring, thresholds and the phrase decision belong to the Api.
/// Nothing about the audio, the transcript or the embedding is logged.
/// </summary>
public sealed class SherpaVerifier : IVerifier, IDisposable
{
    public const string FeatureVersion = "feat-v1";

    private readonly SpeakerEmbeddingExtractor extractor;
    private readonly OfflineRecognizer recognizer;
    private readonly SemaphoreSlim slots;

    public SherpaVerifier(IOptions<SherpaVerifierOptions> options)
    {
        var o = options.Value;
        var lockFile = o.LockFile ?? Path.Combine(o.ModelsDir, "models.lock");
        var info = ModelLock.Load(lockFile, o.ModelsDir);
        ModelId = BuildModelId(info.SpeakerSha256, typeof(SpeakerEmbeddingExtractor).Assembly.GetName().Version?.ToString(3) ?? "unknown");

        extractor = new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig
        {
            Model = Path.Combine(o.ModelsDir, ModelLock.SpeakerModelFile),
            NumThreads = o.Threads,
            Provider = "cpu",
        });

        var asr = new OfflineRecognizerConfig { DecodingMethod = "greedy_search" };
        asr.FeatConfig.SampleRate = AudioQuality.SampleRate;
        asr.FeatConfig.FeatureDim = 80;
        asr.ModelConfig.Whisper.Encoder = Path.Combine(o.ModelsDir, "whisper-base.en", "base.en-encoder.int8.onnx");
        asr.ModelConfig.Whisper.Decoder = Path.Combine(o.ModelsDir, "whisper-base.en", "base.en-decoder.int8.onnx");
        asr.ModelConfig.Whisper.Language = "en";
        asr.ModelConfig.Whisper.Task = "transcribe";
        asr.ModelConfig.Tokens = Path.Combine(o.ModelsDir, "whisper-base.en", "base.en-tokens.txt");
        asr.ModelConfig.NumThreads = o.Threads;
        asr.ModelConfig.Provider = "cpu";
        recognizer = new OfflineRecognizer(asr);

        slots = new SemaphoreSlim(Math.Max(1, o.MaxConcurrent));
    }

    public string ModelId { get; }

    public static string BuildModelId(string speakerSha256, string sherpaVersion) =>
        $"wespeaker-resnet34-lm:{speakerSha256[..12]}+sherpa-{sherpaVersion}+{FeatureVersion}";

    public async Task<VerifierResult> AnalyzeAsync(ReadOnlyMemory<byte> wav, string expectedPhrase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var samples = WavSamples.Read(wav.Span);
        if (samples is null)
        {
            return new VerifierResult(ReasonCode.TooShort, null, null);
        }

        var quality = AudioQuality.Check(samples);
        if (quality is not null)
        {
            return new VerifierResult(quality, null, null);
        }

        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Extract(samples, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    // The native calls cannot be cancelled while they run: the token is checked between steps, and the Api's
    // fail-closed timeout covers a slow call.
    private VerifierResult Extract(float[] samples, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        float[] embedding;
        using (var stream = extractor.CreateStream())
        {
            stream.AcceptWaveform(AudioQuality.SampleRate, samples);
            stream.InputFinished();
            embedding = extractor.Compute(stream);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string transcript;
        using (var stream = recognizer.CreateStream())
        {
            stream.AcceptWaveform(AudioQuality.SampleRate, samples);
            recognizer.Decode(stream);
            transcript = stream.Result.Text;
        }

        return new VerifierResult(null, embedding, transcript);
    }

    public void Dispose()
    {
        extractor.Dispose();
        recognizer.Dispose();
        slots.Dispose();
    }
}
