using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using StepUp.Core;
using StepUp.Infrastructure.Verification;
using StepUp.Verifier.Sherpa;

namespace StepUp.Api.Tests;

/// <summary>
/// The whole API with the real models: enrollment, step-up, a wrong phrase, and a scan of the data directory and the
/// captured logs for audio (M3-A5). Speech is synthesised for each random phrase and re-taken until the real verifier
/// hears it. Needs scripts/fetch-models.sh --test.
/// </summary>
[Trait("Category", "Model")]
public sealed class SherpaApiModelTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string modelsDir = LocateModels();
    private readonly SherpaVerifier verifier;
    private readonly OfflineTts tts;
    private readonly ApiFactory api;

    public SherpaApiModelTests()
    {
        verifier = new SherpaVerifier(Options.Create(new SherpaVerifierOptions { ModelsDir = modelsDir }));
        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = Path.Combine(modelsDir, "tts", "en_US-libritts_r-medium.onnx");
        config.Model.Vits.Tokens = Path.Combine(modelsDir, "tts", "tokens.txt");
        config.Model.Vits.DataDir = Path.Combine(modelsDir, "tts", "espeak-ng-data");
        config.Model.NumThreads = 2;
        tts = new OfflineTts(config);
        api = new ApiFactory(verifier, new() { ["StepUp:ThresholdSeed"] = "0.5" });
    }

    public void Dispose()
    {
        api.Dispose();
        tts.Dispose();
        verifier.Dispose();
    }

    private static string LocateModels()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "models");
            if (File.Exists(Path.Combine(candidate, "models.lock")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("models/models.lock not found. Run scripts/fetch-models.sh --test.");
    }

    /// <summary>Speech of <paramref name="phrase"/> in voice 50, taken again until the real verifier hears the phrase.</summary>
    private async Task<byte[]> SpeakAsync(string phrase)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var audio = tts.Generate(phrase, 1.0f, 50);
            var samples = Resample(audio.Samples, audio.SampleRate, 16_000);
            var wav = ToWav(samples);
            var heard = await verifier.AnalyzeAsync(wav, phrase, Ct);
            if (heard.Reject is null && PhraseGenerator.Matches(heard.Transcript, phrase))
            {
                return wav;
            }
        }

        throw new InvalidOperationException($"No take of '{phrase}' was transcribed correctly in 20 tries.");
    }

    private async Task EnrollAsync(HttpClient client)
    {
        (await client.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct)).EnsureSuccessStatusCode();
        var start = await client.PostAsync("/api/enrollment/start", null, Ct);
        start.EnsureSuccessStatusCode();
        var body = await start.JsonAsync();
        var id = body.GetProperty("enrollmentId").GetGuid();
        foreach (var phrase in body.GetProperty("phrases").EnumerateArray().Select(p => p.GetString()!))
        {
            var sample = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(await SpeakAsync(phrase)), Ct);
            sample.EnsureSuccessStatusCode();
        }

        (await client.PostAsync($"/api/enrollment/complete?enrollmentId={id}", null, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task RealModels_EnrollStepUpAndWrongPhrase_ThroughTheApi_LeaveNoAudioOnDiskOrInLogs()
    {
        using var enrolling = api.ClientFor(api.FreshMfa(Users.Alice));
        await EnrollAsync(enrolling);
        using var client = api.ClientFor(api.TokenFor(Users.Alice));

        // A recording of the right phrase passes and the payout goes through.
        var (id, phrase) = await Flows.ChallengeAsync(client);
        var pass = await Flows.VerifyAsync(client, id, await SpeakAsync(phrase));
        Assert.Equal(HttpStatusCode.OK, pass.StatusCode);
        var assertion = (await pass.JsonAsync()).GetProperty("assertion").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Flows.PayoutAsync(client, assertion)).StatusCode);

        // A recording of another phrase fails, and the log keeps the detail the client never sees (M3-A3).
        var (otherId, otherPhrase) = await Flows.ChallengeAsync(client);
        var wrong = await SpeakAsync(otherPhrase == "apple anchor arrow autumn" ? "bamboo basket beacon bridge" : "apple anchor arrow autumn");
        var fail = await Flows.VerifyAsync(client, otherId, wrong);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fail.StatusCode);
        Assert.Equal("VerificationFailed", await fail.CodeAsync());
        var attempts = await api.Services.GetRequiredService<IAttemptLog>().ListAsync(new AttemptFilter(null, null, null, 0, 100), Ct);
        Assert.Contains(attempts, a => a.Reason == ReasonCode.PhraseMismatch);

        // M3-A5: nothing audio-like in the data directory, nothing from the audio or the speech in the logs.
        var files = Directory.EnumerateFiles(api.DataRoot, "*", SearchOption.AllDirectories).ToList();
        Assert.DoesNotContain(files, f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pcm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
            Assert.DoesNotContain("RIFF", await reader.ReadToEndAsync(Ct), StringComparison.Ordinal);
        }

        string[] logged;
        lock (api.Logs.Lines)
        {
            logged = [.. api.Logs.Lines];
        }

        Assert.DoesNotContain(logged, l => l.Contains(phrase, StringComparison.OrdinalIgnoreCase) || l.Contains(otherPhrase, StringComparison.OrdinalIgnoreCase));
        // Schema names such as the EmbeddingProtected column appear in EF's SQL log (parameters are masked as '?'); values must not.
        var floatRun = new System.Text.RegularExpressions.Regex(@"-?\d+\.\d{3,}([,; ]+-?\d+\.\d{3,}){4,}");
        Assert.DoesNotContain(logged, l => l.Contains("RIFF", StringComparison.Ordinal) || floatRun.IsMatch(l));
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

    private static byte[] ToWav(float[] samples)
    {
        var wav = new byte[44 + (samples.Length * 2)];
        "RIFF"u8.CopyTo(wav);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 16_000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 32_000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), samples.Length * 2);
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp((int)Math.Round(samples[i] * 32767.0), short.MinValue, short.MaxValue);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + (i * 2)), value);
        }

        return wav;
    }
}
