using System.Buffers.Binary;
using System.Text;
using StepUp.Core;

namespace StepUp.Infrastructure.Verification;

public enum FakeQuality : byte { Good = 0, Muted = 1, TooShort = 2, Noisy = 3 }

/// <summary>
/// Builds the audio the <see cref="FakeVerifier"/> understands: a valid 16 kHz mono PCM16 WAV whose samples carry
/// [quality][speaker][phrase utf-8]. Used by tests and the offline demo only. It is not audio.
/// </summary>
public static class FakeAudio
{
    public static byte[] Create(byte speaker, string phrase, FakeQuality quality = FakeQuality.Good)
    {
        var payload = new List<byte> { (byte)quality, speaker };
        payload.AddRange(Encoding.UTF8.GetBytes(phrase));
        if (payload.Count % 2 == 1)
        {
            payload.Add(0);
        }

        var wav = new byte[44 + payload.Count];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + payload.Count);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 16_000);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 32_000);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), payload.Count);
        payload.CopyTo(wav, 44);
        return wav;
    }
}

/// <summary>
/// Deterministic stand-in for the real verifier (M3). The speaker byte picks the embedding, the phrase becomes the
/// transcript. Each utterance adds a small phrase-dependent offset so the same speaker scores below 1.0.
/// </summary>
public sealed class FakeVerifier : IVerifier
{
    private const int Dimensions = 32;

    public string ModelId => "fake-verifier-v1";

    public Task<VerifierResult> AnalyzeAsync(ReadOnlyMemory<byte> wav, string expectedPhrase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = wav.Length > 46 ? wav.Span[44..] : [];
        if (data.Length < 2)
        {
            return Task.FromResult(new VerifierResult(ReasonCode.TooShort, null, null));
        }

        switch ((FakeQuality)data[0])
        {
            case FakeQuality.Muted:
                return Task.FromResult(new VerifierResult(ReasonCode.Muted, null, null));
            case FakeQuality.TooShort:
                return Task.FromResult(new VerifierResult(ReasonCode.TooShort, null, null));
            case FakeQuality.Noisy:
                return Task.FromResult(new VerifierResult(ReasonCode.Noisy, null, null));
        }

        var phrase = Encoding.UTF8.GetString(data[2..]).TrimEnd('\0');
        return Task.FromResult(new VerifierResult(null, Embed(data[1], phrase), phrase));
    }

    private static float[] Embed(byte speaker, string phrase)
    {
        var baseRandom = new Random(speaker * 7919 + 13);
        var noiseRandom = new Random(StableHash(phrase));
        var vector = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(baseRandom.NextDouble() * 2 - 1) + 0.04f * (float)(noiseRandom.NextDouble() * 2 - 1);
        }

        return vector;
    }

    private static int StableHash(string text) => text.Aggregate(17, (h, c) => unchecked(h * 31 + c));
}
