using StepUp.Eval;
using StepUp.Verifier.Sherpa;

namespace StepUp.Eval.Tests;

public class AudioTests
{
    private const int Rate = 16_000;

    private static float[] Sine(double hz, double seconds, double amplitude = 0.5) =>
        [.. Enumerable.Range(0, (int)(seconds * Rate)).Select(n => (float)(amplitude * Math.Sin(2 * Math.PI * hz * n / Rate)))];

    private static double Power(float[] x) => x.Sum(v => (double)v * v) / x.Length;

    [Theory]
    [InlineData(20)]
    [InlineData(10)]
    [InlineData(5)]
    public void White_noise_lands_on_the_requested_snr(double snrDb)
    {
        var signal = Sine(440, 4.0, 0.3);
        var noisy = Audio.AddWhiteNoise(signal, snrDb, new Random(1));

        var noise = noisy.Zip(signal, (a, b) => a - b).ToArray();
        var measured = 10 * Math.Log10(Power(signal) / Power(noise));
        Assert.InRange(measured, snrDb - 0.3, snrDb + 0.3);
    }

    [Fact]
    public void White_noise_is_reproducible_for_a_seed()
    {
        var signal = Sine(440, 1.0);
        Assert.Equal(Audio.AddWhiteNoise(signal, 10, new Random(7)), Audio.AddWhiteNoise(signal, 10, new Random(7)));
    }

    [Fact]
    public void Band_limit_removes_content_above_four_kilohertz_and_keeps_speech_band()
    {
        var high = Audio.BandLimit(Sine(6000, 1.0));
        var low = Audio.BandLimit(Sine(1000, 1.0));

        // ignore the filter edges
        var core = (int)(0.1 * Rate);
        double Db(float[] x, float[] reference) =>
            10 * Math.Log10(Power(x[core..^core]) / Power(reference[core..^core]));

        Assert.True(Db(high, Sine(6000, 1.0)) < -40, "6 kHz should be at least 40 dB down");
        Assert.InRange(Db(low, Sine(1000, 1.0)), -1.0, 1.0);
    }

    [Fact]
    public void Segment_returns_the_requested_slice()
    {
        float[] x = [.. Enumerable.Range(0, 100).Select(n => (float)n)];
        Assert.Equal([10f, 11f, 12f], Audio.Segment(x, 10, 3));
    }

    [Fact]
    public void Encoded_wav_reads_back_through_the_production_reader()
    {
        var signal = Sine(300, 0.5);
        var wav = Audio.ToWav(signal);

        var samples = WavSamples.Read(wav);

        Assert.NotNull(samples);
        Assert.Equal(signal.Length, samples!.Length);
        Assert.Equal(signal[1234], samples[1234], 3);
    }

    [Fact]
    public void Encoded_wav_clips_instead_of_wrapping()
    {
        var wav = Audio.ToWav([2.0f, -2.0f]);
        var samples = WavSamples.Read(wav)!;
        Assert.True(samples[0] > 0.99f);
        Assert.True(samples[1] < -0.99f);
    }
}
