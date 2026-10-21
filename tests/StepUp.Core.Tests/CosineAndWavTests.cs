using System.Buffers.Binary;
using StepUp.Core;

namespace StepUp.Core.Tests;

public class CosineAndWavTests
{
    [Fact]
    public void Cosine_IdenticalVectorsScoreOne()
    {
        Assert.Equal(1.0, Cosine.Similarity([1f, 2f, 3f], [1f, 2f, 3f]), 6);
    }

    [Fact]
    public void Cosine_OrthogonalVectorsScoreZero()
    {
        Assert.Equal(0.0, Cosine.Similarity([1f, 0f], [0f, 1f]), 6);
    }

    [Fact]
    public void Cosine_RejectsLengthMismatch()
    {
        Assert.Throws<ArgumentException>(() => Cosine.Similarity([1f], [1f, 2f]));
    }

    [Fact]
    public void NormalizedMean_HasUnitLengthAndIgnoresInputScale()
    {
        var mean = Cosine.NormalizedMean([[10f, 0f], [0f, 0.1f]]);

        Assert.Equal(1.0, Math.Sqrt(mean.Sum(x => (double)x * x)), 5);
        Assert.Equal(mean[0], mean[1], 5);
    }

    private static byte[] Wav(int seconds = 1, int rate = 16_000, short channels = 1, short bits = 16, short tag = 1)
    {
        var dataLength = seconds * rate * channels * bits / 8;
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + dataLength);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), tag);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), rate);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), bits);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), dataLength);
        return bytes;
    }

    [Fact]
    public void Wav_AcceptsPcm16Mono16k()
    {
        Assert.Equal(WavCheck.Ok, WavInspector.Inspect(Wav()));
    }

    [Fact]
    public void Wav_RejectsOversize()
    {
        Assert.Equal(WavCheck.TooLarge, WavInspector.Inspect(new byte[WavInspector.MaxBytes + 1]));
        Assert.Equal(WavCheck.TooLarge, WavInspector.Inspect(Wav(seconds: 11)));
    }

    [Theory]
    [InlineData(1, 44_100, 1, 16, 1)]
    [InlineData(1, 16_000, 2, 16, 1)]
    [InlineData(1, 16_000, 1, 8, 1)]
    [InlineData(1, 16_000, 1, 16, 3)]
    public void Wav_RejectsBadFormat(int seconds, int rate, short channels, short bits, short tag)
    {
        Assert.Equal(WavCheck.BadFormat, WavInspector.Inspect(Wav(seconds, rate, channels, bits, tag)));
    }

    [Fact]
    public void Wav_RejectsGarbage()
    {
        Assert.Equal(WavCheck.BadFormat, WavInspector.Inspect("not a wav file at all, just text padding.........."u8));
        Assert.Equal(WavCheck.BadFormat, WavInspector.Inspect([]));
    }
}
