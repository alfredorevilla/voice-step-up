using System.Diagnostics;

namespace StepUp.Eval;

public sealed record Utterance(string Id, string Speaker, string Path, string Text);

/// <summary>One extracted LibriSpeech set: <c>&lt;set&gt;/&lt;speaker&gt;/&lt;chapter&gt;/&lt;speaker&gt;-&lt;chapter&gt;-&lt;n&gt;.flac</c> plus one transcript file per chapter.</summary>
public sealed class LibriSpeech(string setDirectory)
{
    public IReadOnlyList<string> Speakers() =>
        [.. Directory.GetDirectories(setDirectory).Select(Path.GetFileName).OfType<string>().OrderBy(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>All utterances of a speaker, ordered by id.</summary>
    public IReadOnlyList<Utterance> Utterances(string speaker)
    {
        var result = new List<Utterance>();
        foreach (var chapter in Directory.GetDirectories(Path.Combine(setDirectory, speaker)))
        {
            foreach (var transcript in Directory.GetFiles(chapter, "*.trans.txt"))
            {
                foreach (var line in File.ReadLines(transcript))
                {
                    var space = line.IndexOf(' ', StringComparison.Ordinal);
                    if (space <= 0)
                    {
                        continue;
                    }

                    var id = line[..space];
                    result.Add(new Utterance(id, speaker, Path.Combine(chapter, id + ".flac"), line[(space + 1)..]));
                }
            }
        }

        return [.. result.OrderBy(u => u.Id, StringComparer.Ordinal)];
    }

    /// <summary>Decode a FLAC file to 16 kHz mono samples in [-1, 1] with <c>ffmpeg</c>.</summary>
    public static float[] Decode(string flacPath)
    {
        var start = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", flacPath, "-f", "s16le", "-ac", "1", "-ar", "16000", "pipe:1" })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        using var buffer = new MemoryStream();
        var errors = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                errors.AppendLine(e.Data);
            }
        };
        process.BeginErrorReadLine();
        process.StandardOutput.BaseStream.CopyTo(buffer);
        process.WaitForExit(); // the no-argument overload also waits for the stderr handler to drain
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed for {Path.GetFileName(flacPath)}: {errors}");
        }

        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes[(2 * i)..]) / 32768f;
        }

        return samples;
    }
}
