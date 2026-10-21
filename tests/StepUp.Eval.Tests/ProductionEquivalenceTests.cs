using Microsoft.Extensions.Options;
using StepUp.Core;
using StepUp.Eval;
using StepUp.Verifier.Sherpa;

namespace StepUp.Eval.Tests;

/// <summary>
/// Protocol section 2: the harness must score exactly as the service does. Needs scripts/fetch-models.sh.
/// Audio is the committed synthetic speech of the verifier tests.
/// </summary>
[Trait("Category", "Model")]
public class ProductionEquivalenceTests
{
    private static readonly Lazy<SherpaVerifier> Shared = new(() =>
        new SherpaVerifier(Options.Create(new SherpaVerifierOptions { ModelsDir = ModelsDir() })));

    private static string ModelsDir()
    {
        var env = Environment.GetEnvironmentVariable("STEPUP_MODELS_DIR");
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "models");
            if (File.Exists(Path.Combine(candidate, "models.lock")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("models/models.lock not found. Run scripts/fetch-models.sh, or set STEPUP_MODELS_DIR.");
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio", name));

    private static async Task<float[]> EmbedAsync(byte[] wav)
    {
        var result = await Shared.Value.AnalyzeAsync(wav, string.Empty, CancellationToken.None);
        Assert.Null(result.Reject);
        return result.Embedding!;
    }

    [Fact]
    public async Task The_harness_wav_encoder_gives_the_embedding_of_the_original_bytes()
    {
        var original = Fixture("voice1-apple-anchor-arrow-autumn.wav");
        var samples = WavSamples.Read(original)!;

        var viaHarness = await EmbedAsync(Audio.ToWav(samples));
        var direct = await EmbedAsync(original);

        Assert.Equal(direct.Length, viaHarness.Length);
        Assert.True(Cosine.Similarity(direct, viaHarness) > 0.99999);
    }

    [Fact]
    public async Task A_harness_trial_score_equals_the_service_composition()
    {
        var enrollmentClips = new[] { "voice1-bamboo-basket-beacon-bridge.wav", "voice1-carpet-summer-noodle-galaxy.wav", "voice1-harbor-camel-otter-dagger.wav" };
        var enrollment = new List<float[]>();
        foreach (var clip in enrollmentClips)
        {
            enrollment.Add(await EmbedAsync(Fixture(clip)));
        }

        var genuineProbe = await EmbedAsync(Fixture("voice1-lantern-meadow-pepper-quartz.wav"));
        var impostorProbe = await EmbedAsync(Fixture("voice2-apple-anchor-arrow-autumn.wav"));

        // what the service does: Cosine.NormalizedMean at enrollment, Cosine.Similarity at verification
        var template = Cosine.NormalizedMean(enrollment);
        var expectedGenuine = Cosine.Similarity(template, genuineProbe);
        var expectedImpostor = Cosine.Similarity(template, impostorProbe);

        var claimed = Speaker(0, enrollment, genuineProbe);
        var other = Speaker(1, enrollment, impostorProbe);
        var trials = Scoring.Trials([claimed, other], "P0");

        var genuine = trials.First(t => t.Claimed == 0 && t.Genuine).Score;
        var impostor = trials.First(t => t.Claimed == 0 && !t.Genuine).Score;
        Assert.Equal(expectedGenuine, genuine, 9);
        Assert.Equal(expectedImpostor, impostor, 9);
        Assert.True(genuine > impostor, "the same synthetic voice should score above a different one");
    }

    private static SpeakerEmbeddings Speaker(int index, List<float[]> enrollment, float[] probe) => new()
    {
        Index = index,
        Id = $"s{index}",
        Primary = [.. Enumerable.Range(0, Design.Repetitions).Select(_ => (float[]?[])[.. enrollment])],
        Secondary = [.. Enumerable.Range(0, Design.Repetitions).Select(_ => (float[]?[])[.. enrollment])],
        Probes = Design.Conditions.ToDictionary(c => c, _ => (float[]?[])[.. Enumerable.Range(0, Design.ProbePool).Select(_ => (float[]?)probe)]),
        ProbeCount = Design.ProbePool,
    };
}
