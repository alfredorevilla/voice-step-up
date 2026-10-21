namespace StepUp.Eval;

/// <summary>Signal handling for the evaluation: slicing, degradations and WAV encoding (16 kHz mono PCM16).</summary>
public static class Audio
{
    public const int SampleRate = 16_000;

    public static float[] Segment(float[] samples, int start, int length) => samples[start..(start + length)];

    public static float[] FirstSeconds(float[] samples, double seconds) =>
        samples.Length <= (int)(seconds * SampleRate) ? samples : samples[..(int)(seconds * SampleRate)];

    /// <summary>White Gaussian noise scaled so that mean signal power over mean noise power equals the SNR.</summary>
    public static float[] AddWhiteNoise(float[] samples, double snrDb, Random rng)
    {
        var power = samples.Sum(v => (double)v * v) / samples.Length;
        var sigma = Math.Sqrt(power / Math.Pow(10.0, snrDb / 10.0));
        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = rng.NextDouble();
            var gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            result[i] = (float)Math.Clamp(samples[i] + (sigma * gaussian), -1.0, 1.0);
        }

        return result;
    }

    /// <summary>
    /// Keeps only what 8 kHz sampling can carry (below 4 kHz): a linear-phase windowed-sinc low-pass at 3.6 kHz.
    /// Equivalent to decimating to 8 kHz and coming back, without the resampling step.
    /// </summary>
    public static float[] BandLimit(float[] samples)
    {
        const int taps = 257;
        const double cutoff = 3600.0 / SampleRate; // cycles per sample
        var kernel = new double[taps];
        var half = taps / 2;
        double sum = 0;
        for (var n = 0; n < taps; n++)
        {
            var m = n - half;
            var sinc = m == 0 ? 2.0 * cutoff : Math.Sin(2.0 * Math.PI * cutoff * m) / (Math.PI * m);
            var window = 0.54 - (0.46 * Math.Cos(2.0 * Math.PI * n / (taps - 1)));
            kernel[n] = sinc * window;
            sum += kernel[n];
        }

        for (var n = 0; n < taps; n++)
        {
            kernel[n] /= sum;
        }

        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            double acc = 0;
            for (var n = 0; n < taps; n++)
            {
                var j = i + n - half;
                if (j >= 0 && j < samples.Length)
                {
                    acc += kernel[n] * samples[j];
                }
            }

            result[i] = (float)acc;
        }

        return result;
    }

    public static byte[] ToWav(float[] samples)
    {
        var dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(SampleRate);
        w.Write(SampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            w.Write((short)Math.Clamp((int)Math.Round(s * 32768.0), short.MinValue, short.MaxValue));
        }

        w.Flush();
        return stream.ToArray();
    }
}
