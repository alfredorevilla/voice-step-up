namespace StepUp.Eval;

/// <summary>The constants of docs/design/evaluation-protocol.md and the seeded choices made from them.</summary>
public static class Design
{
    public const int MasterSeed = 6001;
    public const int EnrollmentPool = 8;
    public const int ProbePool = 30;
    public const int Repetitions = 5;
    public const int SegmentsPerEnrollment = 3;
    public const int ImpostorProbesPerSpeaker = 3;
    public const int MaxRedraws = 10;
    public const double EnrollmentSeconds = 2.5;
    public const double ProbeSeconds = 2.0;
    public const double FullMaxSeconds = 10.0;
    public const double MinUtteranceSeconds = 4.0;
    public const int BootstrapResamples = 2000;

    public static readonly double[] SnrDb = [20, 10, 5];

    /// <summary>Primary condition first. P0 to P4 share the short-segment enrollment, S0 has its own.</summary>
    public static readonly string[] Conditions = ["P0", "P1", "P2", "P3", "P4", "S0"];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["P0"] = "clean, 3 x 2.5 s enrollment, 2 s probe (primary)",
        ["P1"] = "P0 probe + white noise, SNR 20 dB",
        ["P2"] = "P0 probe + white noise, SNR 10 dB",
        ["P3"] = "P0 probe + white noise, SNR 5 dB",
        ["P4"] = "P0 probe band-limited to 4 kHz (8 kHz sampling)",
        ["S0"] = "clean, 3 full utterances (<= 10 s), full-utterance probe (best case)",
    };

    /// <summary><paramref name="count"/> distinct indexes out of <paramref name="of"/>, in the order drawn.</summary>
    public static int[] PickDistinct(Random rng, int of, int count)
    {
        var all = Enumerable.Range(0, of).ToArray();
        for (var i = all.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }

        return all[..count];
    }

    /// <summary>The probes of speaker <paramref name="other"/> used against enrollment (<paramref name="claimed"/>, <paramref name="rep"/>). Independent of the condition, so conditions are paired.</summary>
    public static int[] ImpostorProbeIndexes(int claimed, int rep, int other) =>
        PickDistinct(Seeds.Rng(MasterSeed, "impostor", claimed, rep, other), ProbePool, ImpostorProbesPerSpeaker);

    /// <summary>The enrollment-pool utterances behind repetition <paramref name="rep"/> of a speaker.</summary>
    public static int[] EnrollmentUtterances(string speaker, int rep) =>
        PickDistinct(Seeds.Rng(MasterSeed, "enroll", speaker, rep), EnrollmentPool, SegmentsPerEnrollment);

    /// <summary>Shuffle utterance ids for a speaker; the first qualifying ones form the pools.</summary>
    public static IReadOnlyList<T> ShuffleForPools<T>(string set, string speaker, IReadOnlyList<T> sortedUtterances)
    {
        var order = PickDistinct(Seeds.Rng(MasterSeed, "pool", set, speaker), sortedUtterances.Count, sortedUtterances.Count);
        return [.. order.Select(i => sortedUtterances[i])];
    }
}
