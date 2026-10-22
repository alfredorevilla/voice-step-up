using System.Buffers.Binary;

namespace StepUp.Verifier.Sherpa;

/// <summary>
/// Reads the samples of a PCM16 WAV whose header already passed <see cref="StepUp.Core.WavInspector"/>.
/// Reads only the bytes present, so a header that overstates the data length cannot cause a read past the end.
/// </summary>
public static class WavSamples
{
    /// <summary>Samples scaled to [-1, 1], or null when there is no data chunk.</summary>
    public static float[]? Read(ReadOnlySpan<byte> wav)
    {
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(pos + 4, 4));
            var body = pos + 8;
            if (wav.Slice(pos, 4).SequenceEqual("data"u8))
            {
                var available = (int)Math.Min(size, (uint)(wav.Length - body));
                var count = available / 2;
                var samples = new float[count];
                for (var i = 0; i < count; i++)
                {
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body + (i * 2), 2)) / 32768f;
                }

                return samples;
            }

            var next = (long)pos + 8 + size + (size % 2);
            if (next > wav.Length)
            {
                return null;
            }

            pos = (int)next;
        }

        return null;
    }
}
