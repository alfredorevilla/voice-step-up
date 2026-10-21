using System.Diagnostics;
using Microsoft.Extensions.Options;
using StepUp.Verifier.Sherpa;

namespace StepUp.Eval;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var options = Options.Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine("usage: StepUp.Eval --data <dir> --models <dir> --out <docs dir> [--set=dev-clean --set=test-clean] [--limit-speakers N] [--bootstrap B] [--no-cache] [--protocol-commit HASH]");
            return 2;
        }

        using var verifier = new SherpaVerifier(Microsoft.Extensions.Options.Options.Create(new SherpaVerifierOptions
        {
            ModelsDir = options.Models,
            Threads = 1,
            MaxConcurrent = options.Parallelism,
        }));
        var cache = new Cache(Path.Combine(options.Data, "cache", verifier.ModelId.Replace(':', '_').Replace('+', '_')));

        var results = new Dictionary<string, SetResult>();
        foreach (var set in options.Sets)
        {
            results[set] = await RunSetAsync(set, options, verifier, cache).ConfigureAwait(false);
        }

        if (!results.TryGetValue("dev-clean", out var dev) || !results.TryGetValue("test-clean", out var test))
        {
            Console.Error.WriteLine("Both dev-clean and test-clean are needed: values are chosen on dev-clean and reported on test-clean.");
            return 2;
        }

        Console.Error.WriteLine("Freezing values on dev-clean");
        var frozen = Analysis.FreezeOnDev(dev);
        Console.Error.WriteLine($"  threshold {frozen.Threshold}, strict {frozen.StrictThreshold}, floor {frozen.ConsistencyFloor}, phrase {frozen.PhraseThreshold}, cohort {frozen.CohortAdopted}");

        Console.Error.WriteLine("Summarising");
        var devSummaries = Summaries(dev, frozen, options.Bootstrap);
        var testSummaries = Summaries(test, frozen, options.Bootstrap);
        var input = new ReportInput(dev, test, frozen, devSummaries, testSummaries, options.ProtocolCommit, verifier.ModelId, options.Bootstrap);

        File.WriteAllText(Path.Combine(options.Out, "eval-report.md"), Report.Build(input));
        Report.WriteCsv(Path.Combine(options.Out, "eval"), input);
        Console.Error.WriteLine($"Wrote {Path.Combine(options.Out, "eval-report.md")}");
        return 0;
    }

    private static async Task<SetResult> RunSetAsync(string set, Options options, SherpaVerifier verifier, Cache cache)
    {
        var corpus = new LibriSpeech(Path.Combine(options.Data, "LibriSpeech", set));
        var speakers = corpus.Speakers().Take(options.LimitSpeakers).ToArray();
        var runner = new SpeakerRunner(verifier, corpus, set, options.Parallelism);
        var embeddings = new List<SpeakerEmbeddings>();
        var clock = Stopwatch.StartNew();

        for (var i = 0; i < speakers.Length; i++)
        {
            SpeakerEmbeddings? e = null;
            if (!options.NoCache)
            {
                cache.TryRead(set, speakers[i], out e);
            }

            if (e is null || e.Index != i)
            {
                e = await runner.RunAsync(i, speakers[i], CancellationToken.None).ConfigureAwait(false);
                cache.Write(set, e);
            }

            embeddings.Add(e);
            Console.Error.WriteLine($"[{set}] speaker {i + 1}/{speakers.Length} ({speakers[i]}) done, {clock.Elapsed:mm\\:ss} elapsed");
        }

        Console.Error.WriteLine($"[{set}] phrase check");
        var phrase = await PhraseCheck.RunAsync(verifier, corpus, set, options.Parallelism, CancellationToken.None).ConfigureAwait(false);
        Console.Error.WriteLine($"[{set}] scoring");
        var built = SetResult.Build(set, embeddings, phrase);
        foreach (var c in Design.Conditions)
        {
            var genuine = built.Trials[c].Count(t => t.Genuine);
            var missing = embeddings.Sum(e => e.Probes[c].Count(p => p is null));
            Console.Error.WriteLine($"[{set}] {c}: {genuine} genuine, {built.Trials[c].Count - genuine} impostor trials, {missing} probes not acquired");
        }

        Console.Error.WriteLine($"[{set}] phrase cases: {phrase.Selected} selected of {phrase.Candidates} candidates, {phrase.RejectedByGate} refused by the gate");
        return built;
    }

    private static Dictionary<string, ConditionSummary> Summaries(SetResult set, Frozen frozen, int resamples)
    {
        var map = new System.Collections.Concurrent.ConcurrentDictionary<string, ConditionSummary>();
        Parallel.ForEach(Design.Conditions, c => map[c] = Analysis.Summarise(set, c, frozen, resamples));
        return new Dictionary<string, ConditionSummary>(map);
    }

    private sealed record Options(
        string Data,
        string Models,
        string Out,
        IReadOnlyList<string> Sets,
        int LimitSpeakers,
        int Bootstrap,
        bool NoCache,
        string ProtocolCommit,
        int Parallelism)
    {
        public static Options? Parse(string[] args)
        {
            string? data = null, models = null, output = null;
            var sets = new List<string>();
            var limit = int.MaxValue;
            var bootstrap = Design.BootstrapResamples;
            var noCache = false;
            var commit = "unknown";
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--data": data = args[++i]; break;
                    case "--models": models = args[++i]; break;
                    case "--out": output = args[++i]; break;
                    case "--limit-speakers": limit = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--bootstrap": bootstrap = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--protocol-commit": commit = args[++i]; break;
                    case "--no-cache": noCache = true; break;
                    default:
                        if (a.StartsWith("--set=", StringComparison.Ordinal))
                        {
                            sets.Add(a["--set=".Length..]);
                        }
                        else
                        {
                            return null;
                        }

                        break;
                }
            }

            if (data is null || models is null || output is null)
            {
                return null;
            }

            if (sets.Count == 0)
            {
                sets = ["dev-clean", "test-clean"];
            }

            sets.Sort(StringComparer.Ordinal);
            return new Options(data, models, output, sets, limit, bootstrap, noCache, commit, Math.Max(1, Environment.ProcessorCount - 2));
        }
    }
}
