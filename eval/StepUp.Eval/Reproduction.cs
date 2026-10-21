using System.Globalization;

namespace StepUp.Eval;

/// <summary>Amendment 2 (A2.1): P0 to P4 and S0 must reproduce the numbers of the first report. Compares a new run with the earlier summary.csv.</summary>
public static class Reproduction
{
    private static readonly string[] Original = ["P0", "P1", "P2", "P3", "P4", "S0"];

    /// <summary>One sentence for the report: matches, or the first differences. An absent or unreadable earlier file is said so.</summary>
    public static string Compare(
        string? earlierCsv,
        IReadOnlyDictionary<string, ConditionSummary> devSummaries,
        IReadOnlyDictionary<string, ConditionSummary> testSummaries)
    {
        if (string.IsNullOrWhiteSpace(earlierCsv))
        {
            return "No earlier summary.csv was available, so the reproduction of P0 to P4 and S0 against the first report was not checked.";
        }

        var rows = earlierCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.TrimEnd('\r').Split(',')).Where(c => c.Length >= 21).ToArray();
        if (rows.Length == 0)
        {
            return "The earlier summary.csv had no rows, so the reproduction of P0 to P4 and S0 was not checked.";
        }

        var differences = new List<string>();
        var compared = 0;
        foreach (var row in rows)
        {
            if (!Original.Contains(row[1]))
            {
                continue;
            }

            var summaries = row[0] == "dev-clean" ? devSummaries : testSummaries;
            if (!summaries.TryGetValue(row[1], out var s))
            {
                continue;
            }

            var point = row[2] == "strict" ? s.AtStrict : s.AtDefault;
            var now = new[]
            {
                point.Threshold.ToString("F2", CultureInfo.InvariantCulture), s.GenuineTrials.ToString(CultureInfo.InvariantCulture), s.ImpostorTrials.ToString(CultureInfo.InvariantCulture),
                point.Far.ToString("F6", CultureInfo.InvariantCulture), point.Frr.ToString("F6", CultureInfo.InvariantCulture), s.Eer.ToString("F6", CultureInfo.InvariantCulture),
            };
            var before = new[] { row[3], row[4], row[5], row[8], row[13], row[18] };
            var names = new[] { "threshold", "genuine trials", "impostor trials", "FAR", "FRR", "EER" };
            for (var k = 0; k < now.Length; k++)
            {
                compared++;
                if (now[k] != before[k])
                {
                    differences.Add($"{row[0]} {row[1]} {row[2]} {names[k]}: {before[k]} before, {now[k]} now");
                }
            }
        }

        return differences.Count == 0
            ? $"P0 to P4 and S0 reproduce the first report exactly ({compared} values compared: thresholds, trial counts, FAR, FRR and EER, dev-clean and test-clean)."
            : $"P0 to P4 and S0 do NOT reproduce the first report exactly: {differences.Count} of {compared} values differ, for example {string.Join("; ", differences.Take(3))}.";
    }
}
