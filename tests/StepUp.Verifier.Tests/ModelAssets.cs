using System.Buffers.Binary;

namespace StepUp.Verifier.Tests;

/// <summary>
/// Locates the fetched runtime models (scripts/fetch-models.sh) and the committed synthetic speech fixtures in Audio/.
/// The fixtures are text-to-speech output, not recordings of people; see Audio/README.md.
/// </summary>
internal static class ModelAssets
{
    private static readonly Lazy<string> Dir = new(Locate);

    public static string ModelsDir => Dir.Value;

    private static string Locate()
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

    public static string FixtureName(int speaker, string phrase) => $"voice{speaker}-{phrase.Replace(' ', '-')}.wav";

    /// <summary>A 16 kHz mono PCM16 WAV of <paramref name="phrase"/> in one of three stable synthetic voices.</summary>
    public static byte[] Speech(int speaker, string phrase) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio", FixtureName(speaker, phrase)));

    public static byte[] Silence(double seconds) => ToWav(new float[(int)(seconds * 16_000)]);

    public static byte[] Truncate(byte[] wav, double seconds)
    {
        var samples = StepUp.Verifier.Sherpa.WavSamples.Read(wav)!;
        return ToWav(samples.Take((int)(seconds * 16_000)).ToArray());
    }

    public static byte[] ToWav(float[] samples)
    {
        var wav = new byte[44 + (samples.Length * 2)];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 16_000);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 32_000);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), samples.Length * 2);
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp((int)Math.Round(samples[i] * 32767.0), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + (i * 2)), value);
        }

        return wav;
    }
}
