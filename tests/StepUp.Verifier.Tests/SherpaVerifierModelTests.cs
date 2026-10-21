using System.Diagnostics;
using Microsoft.Extensions.Options;
using StepUp.Core;
using StepUp.Verifier.Sherpa;

namespace StepUp.Verifier.Tests;

/// <summary>Behaviour specific to the sherpa-onnx verifier, on the real models. Needs scripts/fetch-models.sh --test.</summary>
[Trait("Category", "Model")]
public sealed class SherpaVerifierModelTests : IDisposable
{
    private const string Phrase = "apple anchor arrow autumn";
    private const string OtherPhrase = "bamboo basket beacon bridge";

    private readonly SherpaVerifier verifier = New(maxConcurrent: 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SherpaVerifier New(int maxConcurrent) =>
        new(Options.Create(new SherpaVerifierOptions { ModelsDir = ModelAssets.ModelsDir, MaxConcurrent = maxConcurrent }));

    public void Dispose() => verifier.Dispose();

    [Fact]
    public void ModelId_NamesTheSpeakerModelHashPrefixAndTheSherpaPackageVersion()
    {
        var lockInfo = ModelLock.Load(Path.Combine(ModelAssets.ModelsDir, "models.lock"), ModelAssets.ModelsDir);

        Assert.Equal($"wespeaker-resnet34-lm:{lockInfo.SpeakerSha256[..12]}+sherpa-1.13.8+feat-v1", verifier.ModelId);
    }

    [Fact]
    public async Task Analyze_ClippedSpeech_IsRejectedAsNoisy()
    {
        var samples = WavSamples.Read(ModelAssets.Speech(1, Phrase))!;
        for (var i = 0; i < samples.Length; i += 20)
        {
            samples[i] = i % 40 == 0 ? 1.0f : -1.0f;
        }

        var result = await verifier.AnalyzeAsync(ModelAssets.ToWav(samples), Phrase, Ct);

        Assert.Equal(ReasonCode.Noisy, result.Reject);
        Assert.Null(result.Embedding);
        Assert.Null(result.Transcript);
    }

    [Fact]
    public async Task Analyze_LoudNoise_IsRejectedAsNoisy()
    {
        var rng = new Random(11);
        var noise = Enumerable.Range(0, 32_000).Select(_ => (float)((rng.NextDouble() * 2) - 1) * 0.1f).ToArray();

        var result = await verifier.AnalyzeAsync(ModelAssets.ToWav(noise), Phrase, Ct);

        Assert.Equal(ReasonCode.Noisy, result.Reject);
    }

    [Fact]
    public async Task Analyze_APhraseSpokenAgainstAnotherChallenge_DoesNotMatch()
    {
        var result = await verifier.AnalyzeAsync(ModelAssets.Speech(1, OtherPhrase), Phrase, Ct);

        Assert.Null(result.Reject);
        Assert.False(PhraseGenerator.Matches(result.Transcript, Phrase));
        Assert.True(PhraseGenerator.Matches(result.Transcript, OtherPhrase));
    }

    [Fact]
    public async Task Analyze_LatencyForEmbeddingPlusAsrStaysFarInsideTheThreeSecondTimeout()
    {
        var phrases = new[] { Phrase, OtherPhrase, "lantern meadow pepper quartz", "harbor camel otter dagger", "carpet summer noodle galaxy" };
        var audio = phrases.Select(p => ModelAssets.Speech(1, p)).ToArray();
        _ = await verifier.AnalyzeAsync(audio[0], Phrase, Ct); // warm up

        var elapsed = new List<double>();
        foreach (var wav in audio.Concat(audio))
        {
            var clock = Stopwatch.StartNew();
            var result = await verifier.AnalyzeAsync(wav, Phrase, Ct);
            clock.Stop();
            Assert.Null(result.Reject);
            elapsed.Add(clock.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();
        var p95 = elapsed[(int)Math.Ceiling(elapsed.Count * 0.95) - 1];
        Assert.True(p95 < 3000, $"p95 {p95:F0} ms");
    }

    [Fact]
    public async Task Analyze_ConcurrentCalls_GiveTheSameResultsAsSequentialOnes()
    {
        var wav = ModelAssets.Speech(1, Phrase);
        var expected = await verifier.AnalyzeAsync(wav, Phrase, Ct);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => verifier.AnalyzeAsync(wav, Phrase, Ct)));

        Assert.All(results, r =>
        {
            Assert.Equal(expected.Embedding, r.Embedding);
            Assert.Equal(expected.Transcript, r.Transcript);
        });
    }

    [Fact]
    public async Task Analyze_WithOneSlot_StillCompletesEveryCall()
    {
        using var single = New(maxConcurrent: 1);
        var wav = ModelAssets.Speech(1, Phrase);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => single.AnalyzeAsync(wav, Phrase, Ct)));

        Assert.All(results, r => Assert.Null(r.Reject));
    }

    [Fact]
    public async Task Analyze_CancelledWhileWaitingForASlot_Throws()
    {
        using var single = New(maxConcurrent: 1);
        var wav = ModelAssets.Speech(1, Phrase);
        using var cts = new CancellationTokenSource();
        var running = Enumerable.Range(0, 3).Select(_ => single.AnalyzeAsync(wav, Phrase, Ct)).ToArray();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => single.AnalyzeAsync(wav, Phrase, cts.Token));
        await Task.WhenAll(running);
    }

    [Fact]
    public async Task Analyze_WritesNoAudioToDiskAndNoNewFilesAnywhereItCouldPutThem()
    {
        var watched = new[] { ModelAssets.ModelsDir, Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetTempPath() };
        var before = Snapshot(watched);

        for (var i = 0; i < 3; i++)
        {
            _ = await verifier.AnalyzeAsync(ModelAssets.Speech(1, Phrase), Phrase, Ct);
        }

        var created = Snapshot(watched).Except(before).Where(f => !f.Contains("modellock", StringComparison.Ordinal)).ToList();
        var audioFiles = created.Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".pcm", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(audioFiles);
        Assert.DoesNotContain(created, f => f.StartsWith(ModelAssets.ModelsDir, StringComparison.Ordinal));
    }

    private static HashSet<string> Snapshot(string[] roots)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots.Distinct())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                files.Add(file);
            }
        }

        return files;
    }
}
