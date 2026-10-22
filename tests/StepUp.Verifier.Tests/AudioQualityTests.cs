using StepUp.Core;
using StepUp.Verifier.Sherpa;

namespace StepUp.Verifier.Tests;

/// <summary>Quality gates on synthetic signals (docs/design/verifier.md section 3). No model needed.</summary>
public class AudioQualityTests
{
    private const int Rate = 16_000;

    /// <summary>Alternating 100 ms bursts of band-limited noise at <paramref name="burst"/> and gaps at <paramref name="floor"/>, over <paramref name="seconds"/>.</summary>
    private static float[] SpeechLike(double seconds, double burst, double floor, int seed = 7)
    {
        var rng = new Random(seed);
        var samples = new float[(int)(seconds * Rate)];
        for (var i = 0; i < samples.Length; i++)
        {
            var inBurst = (i / (Rate / 10)) % 2 == 0;
            var amplitude = inBurst ? burst : floor;
            samples[i] = (float)((rng.NextDouble() * 2 - 1) * amplitude);
        }

        return samples;
    }

    private static float[] Noise(double seconds, double amplitude, int seed = 3)
    {
        var rng = new Random(seed);
        return [.. Enumerable.Range(0, (int)(seconds * Rate)).Select(_ => (float)((rng.NextDouble() * 2 - 1) * amplitude))];
    }

    [Fact]
    public void Silence_IsMuted()
    {
        Assert.Equal(ReasonCode.Muted, AudioQuality.Check(new float[2 * Rate]));
    }

    [Fact]
    public void VeryQuietNoise_IsMuted()
    {
        Assert.Equal(ReasonCode.Muted, AudioQuality.Check(Noise(2, 0.003)));
    }

    [Fact]
    public void LowPeakEvenWithSomeEnergy_IsMuted()
    {
        Assert.Equal(ReasonCode.Muted, AudioQuality.Check(Noise(2, 0.008)));
    }

    [Fact]
    public void ClippedSpeech_IsNoisy()
    {
        var samples = SpeechLike(2, 0.2, 0.001);
        for (var i = 0; i < samples.Length; i += 40)
        {
            samples[i] = i % 80 == 0 ? 1.0f : -1.0f; // 2.5% of samples at full scale
        }

        Assert.Equal(ReasonCode.Noisy, AudioQuality.Check(samples));
    }

    [Fact]
    public void ShortBurstOfSpeech_IsTooShort()
    {
        var samples = new float[2 * Rate];
        var burst = SpeechLike(0.4, 0.2, 0.2);
        Array.Copy(burst, 0, samples, Rate / 2, burst.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] += (float)((i % 7 - 3) * 0.0002); // a quiet floor so the clip is not digital silence
        }

        Assert.Equal(ReasonCode.TooShort, AudioQuality.Check(samples));
    }

    [Fact]
    public void LoudSteadyNoise_IsNoisyNotTooShort()
    {
        Assert.Equal(ReasonCode.Noisy, AudioQuality.Check(Noise(2, 0.1)));
    }

    [Fact]
    public void SpeechOverModerateNoise_BelowTenDbSnr_IsNoisy()
    {
        // Bursts about 8.6 dB over the floor: frames count as speech (floor + 6 dB) but the SNR gate (10 dB) fails.
        Assert.Equal(ReasonCode.Noisy, AudioQuality.Check(SpeechLike(2, 0.3, 0.12)));
    }

    [Fact]
    public void CleanSpeechLikeSignal_Passes()
    {
        Assert.Null(AudioQuality.Check(SpeechLike(2, 0.2, 0.002)));
    }

    [Fact]
    public void SpeechJustUnderTheNetSpeechMinimum_IsTooShort()
    {
        // 1.4 s of alternating 100 ms bursts = 0.7 s of speech frames, under the 0.8 s minimum.
        Assert.Equal(ReasonCode.TooShort, AudioQuality.Check(SpeechLike(1.4, 0.2, 0.002)));
    }

    [Fact]
    public void SpeechJustOverTheNetSpeechMinimum_Passes()
    {
        // 1.8 s = 0.9 s of speech frames.
        Assert.Null(AudioQuality.Check(SpeechLike(1.8, 0.2, 0.002)));
    }

    [Fact]
    public void EmptyAudio_IsTooShort()
    {
        Assert.Equal(ReasonCode.TooShort, AudioQuality.Check([]));
    }

    [Fact]
    public void WavSamples_ReadsPcm16AndIgnoresTrailingOddByte()
    {
        var wav = StepUp.Infrastructure.Verification.FakeAudio.Create(1, "a b", StepUp.Infrastructure.Verification.FakeQuality.Good);

        var samples = WavSamples.Read(wav);

        Assert.NotNull(samples);
        Assert.All(samples, s => Assert.InRange(s, -1.0f, 1.0f));
        Assert.Equal((wav.Length - 44) / 2, samples.Length);
    }

    [Fact]
    public void WavSamples_ReadsOnlyTheBytesPresentWhenTheHeaderOverstates()
    {
        var wav = StepUp.Infrastructure.Verification.FakeAudio.Create(1, "abcdef");
        var truncated = wav[..(wav.Length - 4)];

        var samples = WavSamples.Read(truncated);

        Assert.NotNull(samples);
        Assert.Equal((truncated.Length - 44) / 2, samples.Length);
    }

    [Fact]
    public void WavSamples_ReturnsNullWithoutADataChunk()
    {
        Assert.Null(WavSamples.Read(new byte[100]));
    }
}
