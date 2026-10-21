using StepUp.Core;
using StepUp.Verifier.Sherpa;

namespace StepUp.Eval;

/// <summary>Everything the scoring needs from one speaker. A null embedding is a failure to acquire (the quality gate refused the audio).</summary>
public sealed class SpeakerEmbeddings
{
    public required int Index { get; init; }

    public required string Id { get; init; }

    /// <summary>Short-segment enrollments: [repetition][segment].</summary>
    public required float[]?[][] Primary { get; init; }

    /// <summary>Full-utterance enrollments: [repetition][segment].</summary>
    public required float[]?[][] Secondary { get; init; }

    /// <summary>Per condition, the embedding of each of the speaker's probes.</summary>
    public required IReadOnlyDictionary<string, float[]?[]> Probes { get; init; }

    /// <summary>How many probe utterances this speaker has (at most <see cref="Design.ProbePool"/>).</summary>
    public required int ProbeCount { get; init; }

    public int SegmentRedraws { get; init; }
}

/// <summary>Cuts the audio of one speaker by the protocol and embeds it with the production verifier.</summary>
public sealed class SpeakerRunner(IVerifier verifier, LibriSpeech corpus, string set, int parallelism)
{
    public async Task<SpeakerEmbeddings> RunAsync(int index, string speaker, CancellationToken cancellationToken)
    {
        var (enrollmentPool, probePool) = await LoadPoolsAsync(speaker).ConfigureAwait(false);
        var redraws = 0;

        var jobs = new List<(float[] Samples, Action<float[]?> Store)>();
        var primary = NewGrid(Design.Repetitions, Design.SegmentsPerEnrollment);
        var secondary = NewGrid(Design.Repetitions, Design.SegmentsPerEnrollment);
        var probes = Design.Conditions.ToDictionary(c => c, _ => new float[]?[probePool.Count]);

        for (var rep = 0; rep < Design.Repetitions; rep++)
        {
            var chosen = Design.EnrollmentUtterances(speaker, rep);
            for (var k = 0; k < chosen.Length; k++)
            {
                var utterance = enrollmentPool[chosen[k]];
                var (segment, tries) = DrawSegment(utterance, Design.EnrollmentSeconds, Seeds.Rng(Design.MasterSeed, "seg", speaker, "E", rep, k));
                redraws += tries - 1;
                var (r, kk) = (rep, k);
                if (segment is not null)
                {
                    jobs.Add((segment, e => primary[r][kk] = e));
                }

                var full = Audio.FirstSeconds(utterance, Design.FullMaxSeconds);
                jobs.Add((full, e => secondary[r][kk] = e));
            }
        }

        for (var i = 0; i < probePool.Count; i++)
        {
            var utterance = probePool[i];
            var (clean, tries) = DrawSegment(utterance, Design.ProbeSeconds, Seeds.Rng(Design.MasterSeed, "seg", speaker, "Q", i));
            redraws += tries - 1;
            var n = i;
            if (clean is not null)
            {
                jobs.Add((clean, e => probes["P0"][n] = e));
                for (var s = 0; s < Design.SnrDb.Length; s++)
                {
                    var snr = Design.SnrDb[s];
                    var noisy = Audio.AddWhiteNoise(clean, snr, Seeds.Rng(Design.MasterSeed, "noise", speaker, i, snr));
                    var condition = $"P{s + 1}";
                    jobs.Add((noisy, e => probes[condition][n] = e));
                }

                jobs.Add((Audio.BandLimit(clean), e => probes["P4"][n] = e));
            }

            jobs.Add((Audio.FirstSeconds(utterance, Design.FullMaxSeconds), e => probes["S0"][n] = e));
        }

        await Parallel.ForEachAsync(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (job, ct) =>
            {
                var result = await verifier.AnalyzeAsync(Audio.ToWav(job.Samples), string.Empty, ct).ConfigureAwait(false);
                job.Store(result.Reject is null ? result.Embedding : null);
            }).ConfigureAwait(false);

        return new SpeakerEmbeddings
        {
            Index = index,
            Id = speaker,
            Primary = primary,
            Secondary = secondary,
            Probes = probes,
            ProbeCount = probePool.Count,
            SegmentRedraws = redraws,
        };
    }

    /// <summary>A segment that passes the production quality gate, or null after <see cref="Design.MaxRedraws"/> tries.</summary>
    public static (float[]? Segment, int Tries) DrawSegment(float[] utterance, double seconds, Random rng)
    {
        var length = (int)(seconds * Audio.SampleRate);
        for (var attempt = 1; attempt <= Design.MaxRedraws; attempt++)
        {
            var start = rng.Next(0, utterance.Length - length + 1);
            var segment = Audio.Segment(utterance, start, length);
            if (AudioQuality.Check(segment) is null)
            {
                return (segment, attempt);
            }
        }

        return (null, Design.MaxRedraws);
    }

    private async Task<(IReadOnlyList<float[]> Enrollment, IReadOnlyList<float[]> Probe)> LoadPoolsAsync(string speaker)
    {
        var shuffled = Design.ShuffleForPools(set, speaker, corpus.Utterances(speaker));
        var needed = Design.EnrollmentPool + Design.ProbePool;
        var decoded = new List<float[]>();
        var next = 0;
        while (decoded.Count < needed && next < shuffled.Count)
        {
            var batch = shuffled.Skip(next).Take(needed).ToArray();
            next += batch.Length;
            var audio = await Task.WhenAll(batch.Select(u => Task.Run(() => LibriSpeech.Decode(u.Path)))).ConfigureAwait(false);
            decoded.AddRange(audio.Where(a => a.Length >= (int)(Design.MinUtteranceSeconds * Audio.SampleRate)));
        }

        if (decoded.Count < Design.EnrollmentPool + Design.MinProbePool)
        {
            throw new InvalidOperationException($"Speaker {speaker} has only {decoded.Count} utterances of at least {Design.MinUtteranceSeconds} s; {Design.EnrollmentPool + Design.MinProbePool} are the minimum.");
        }

        return (decoded.Take(Design.EnrollmentPool).ToArray(), decoded.Skip(Design.EnrollmentPool).Take(Design.ProbePool).ToArray());
    }

    private static float[]?[][] NewGrid(int rows, int columns) =>
        [.. Enumerable.Range(0, rows).Select(_ => new float[columns][])];
}
