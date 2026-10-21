using System.Buffers.Binary;

namespace StepUp.Core;

public enum WavCheck { Ok, TooLarge, BadFormat }

/// <summary>Header-only check before any parsing of samples: PCM16, mono, 16 kHz, at most 10 s and 1 MB.</summary>
public static class WavInspector
{
    public const int MaxBytes = 1_000_000;
    public const int MaxSeconds = 10;
    public const int SampleRate = 16_000;

    public static WavCheck Inspect(ReadOnlySpan<byte> wav)
    {
        if (wav.Length > MaxBytes)
        {
            return WavCheck.TooLarge;
        }

        if (wav.Length < 44 || !wav[..4].SequenceEqual("RIFF"u8) || !wav.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return WavCheck.BadFormat;
        }

        var formatFound = false;
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = wav.Slice(pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(pos + 4, 4));
            var body = pos + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + 16 > wav.Length)
                {
                    return WavCheck.BadFormat;
                }

                var tag = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body, 2));
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 2, 2));
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(body + 4, 4));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 14, 2));
                if (tag != 1 || channels != 1 || rate != SampleRate || bits != 16)
                {
                    return WavCheck.BadFormat;
                }

                formatFound = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (!formatFound)
                {
                    return WavCheck.BadFormat;
                }

                var available = (long)Math.Min(size, (uint)(wav.Length - body));
                return available / 2.0 / SampleRate > MaxSeconds ? WavCheck.TooLarge : WavCheck.Ok;
            }

            var next = (long)pos + 8 + size + (size % 2);
            if (next > wav.Length)
            {
                return WavCheck.BadFormat;
            }

            pos = (int)next;
        }

        return WavCheck.BadFormat;
    }
}
