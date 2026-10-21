using Microsoft.Extensions.Options;
using SherpaOnnx;
using StepUp.Core;
using StepUp.Verifier.Sherpa;

namespace StepUp.Verifier.Tests;

/// <summary>
/// Regenerates the synthetic speech fixtures in Audio/. Runs only when STEPUP_GENERATE_FIXTURES=1 and the test models
/// are fetched (scripts/fetch-models.sh --test). The Piper text-to-speech output differs on every call, so each clip is
/// generated until the real verifier hears the right phrase, and the set is accepted only if the voices separate.
/// </summary>
[Trait("Category", "Model")]
public sealed class FixtureGenerator
{
    private const string P1 = "apple anchor arrow autumn";
    private const string P2 = "bamboo basket beacon bridge";

    // Piper LibriTTS-R voices 50, 162 and 148 for speakers 1, 2 and 3 (voice sweep in docs/design/verifier.md).
    private static readonly Dictionary<int, int> Voices = new() { [1] = 50, [2] = 162, [3] = 148 };

    private static readonly (int Speaker, string Phrase)[] Manifest =
    [
        (1, P1), (1, P2), (1, "lantern meadow pepper quartz"), (1, "harbor camel otter dagger"),
        (1, "carpet summer noodle galaxy"), (1, "apple"), (2, P1), (3, P1),
    ];

    [Fact]
    public async Task Generate()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STEPUP_GENERATE_FIXTURES") == "1", "Set STEPUP_GENERATE_FIXTURES=1 to regenerate the fixtures.");
        var ct = TestContext.Current.CancellationToken;
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Audio"));
        Directory.CreateDirectory(output);

        var tts = new OfflineTtsConfig();
        tts.Model.Vits.Model = Path.Combine(ModelAssets.ModelsDir, "tts", "en_US-libritts_r-medium.onnx");
        tts.Model.Vits.Tokens = Path.Combine(ModelAssets.ModelsDir, "tts", "tokens.txt");
        tts.Model.Vits.DataDir = Path.Combine(ModelAssets.ModelsDir, "tts", "espeak-ng-data");
        tts.Model.NumThreads = 2;
        using var engine = new OfflineTts(tts);
        using var verifier = new SherpaVerifier(Options.Create(new SherpaVerifierOptions { ModelsDir = ModelAssets.ModelsDir }));

        for (var set = 0; set < 20; set++)
        {
            var clips = new Dictionary<(int, string), byte[]>();
            foreach (var (speaker, phrase) in Manifest)
            {
                var wav = await TakeAsync(engine, verifier, speaker, phrase, ct);
                clips[(speaker, phrase)] = wav;
            }

            var enrolled = (await verifier.AnalyzeAsync(clips[(1, P2)], P2, ct)).Embedding!;
            var same = (await verifier.AnalyzeAsync(clips[(1, P1)], P1, ct)).Embedding!;
            var other = (await verifier.AnalyzeAsync(clips[(2, P1)], P1, ct)).Embedding!;
            var margin = Cosine.Similarity(enrolled, same) - Cosine.Similarity(enrolled, other);
            if (margin < 0.2)
            {
                continue;
            }

            foreach (var ((speaker, phrase), wav) in clips)
            {
                await File.WriteAllBytesAsync(Path.Combine(output, ModelAssets.FixtureName(speaker, phrase)), wav, ct);
            }

            return;
        }

        Assert.Fail("No set of clips with a same-versus-different cosine margin of 0.2 in 20 tries.");
    }

    private static async Task<byte[]> TakeAsync(OfflineTts engine, SherpaVerifier verifier, int speaker, string phrase, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            var audio = engine.Generate(phrase, 1.0f, Voices[speaker]);
            var wav = ModelAssets.ToWav(Resample(audio.Samples, audio.SampleRate, 16_000));
            if (phrase == "apple")
            {
                return wav; // only ever truncated to 0.3 s for the too-short case
            }

            var result = await verifier.AnalyzeAsync(wav, phrase, ct);
            if (result.Reject is null && PhraseGenerator.Matches(result.Transcript, phrase))
            {
                return wav;
            }
        }

        throw new InvalidOperationException($"No take of '{phrase}' by voice {speaker} was transcribed correctly in 15 tries.");
    }

    private static float[] Resample(float[] x, int from, int to)
    {
        var n = (int)((long)x.Length * to / from);
        var y = new float[n];
        for (var i = 0; i < n; i++)
        {
            var position = (double)i * from / to;
            var a = (int)position;
            var fraction = (float)(position - a);
            var b = Math.Min(a + 1, x.Length - 1);
            y[i] = (x[a] * (1 - fraction)) + (x[b] * fraction);
        }

        return y;
    }
}
