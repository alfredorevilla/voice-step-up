using StepUp.Core;

namespace StepUp.Verifier.Sherpa;

/// <summary>
/// Energy-based quality gates (docs/design/verifier.md section 3). Cheap, deterministic and run before any model.
/// Not a speech detector: music or a TV in the background can pass.
/// </summary>
public static class AudioQuality
{
    public const int SampleRate = 16_000;
    public const int FrameSamples = SampleRate / 50; // 20 ms

    public const float MinPeak = 0.01f; // -40 dBFS
    public const double MinRms = 0.003; // -50 dBFS
    public const double ClipMagnitude = 32700.0 / 32768.0;
    public const double MaxClippedFraction = 0.01;
    public const double SpeechOverFloorDb = 6.0;
    public const double SpeechAbsoluteFloorDb = -45.0;
    public const double MinNetSpeechSeconds = 0.8;
    public const double LoudNonSpeechMedianDb = -40.0;
    public const double LoudNonSpeechMaxSpeechSeconds = 0.2;
    public const double MinSnrDb = 10.0;

    /// <summary>Null when the audio is usable, otherwise the reason it is not.</summary>
    public static ReasonCode? Check(float[] samples)
    {
        if (samples.Length < FrameSamples)
        {
            return ReasonCode.TooShort;
        }

        double sumSquares = 0;
        float peak = 0;
        var clipped = 0;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);
            peak = Math.Max(peak, magnitude);
            sumSquares += (double)sample * sample;
            if (magnitude >= ClipMagnitude)
            {
                clipped++;
            }
        }

        if (peak < MinPeak || Math.Sqrt(sumSquares / samples.Length) < MinRms)
        {
            return ReasonCode.Muted;
        }

        if ((double)clipped / samples.Length >= MaxClippedFraction)
        {
            return ReasonCode.Noisy;
        }

        var frames = samples.Length / FrameSamples;
        var power = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var i = f * FrameSamples; i < (f + 1) * FrameSamples; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            power[f] = Math.Max(sum / FrameSamples, 1e-10);
        }

        var levels = power.Select(ToDb).ToArray();
        var sorted = (double[])levels.Clone();
        Array.Sort(sorted);
        var noiseFloorDb = sorted[(int)(frames * 0.10)];
        var speechThreshold = Math.Max(noiseFloorDb + SpeechOverFloorDb, SpeechAbsoluteFloorDb);

        var speechPower = 0.0;
        var speechFrames = 0;
        for (var f = 0; f < frames; f++)
        {
            if (levels[f] >= speechThreshold)
            {
                speechFrames++;
                speechPower += power[f];
            }
        }

        var netSpeechSeconds = speechFrames * (double)FrameSamples / SampleRate;
        if (netSpeechSeconds < MinNetSpeechSeconds)
        {
            var loudNonSpeech = netSpeechSeconds < LoudNonSpeechMaxSpeechSeconds && sorted[frames / 2] >= LoudNonSpeechMedianDb;
            return loudNonSpeech ? ReasonCode.Noisy : ReasonCode.TooShort;
        }

        var quietest = Math.Max(1, frames / 10);
        var noisePower = power.OrderBy(p => p).Take(quietest).Average();
        var snrDb = ToDb(speechPower / speechFrames) - ToDb(noisePower);
        return snrDb < MinSnrDb ? ReasonCode.Noisy : null;
    }

    private static double ToDb(double meanSquare) => 10.0 * Math.Log10(Math.Max(meanSquare, 1e-10));
}
