using System.Text.RegularExpressions;

namespace StepUp.Core.Tests;

/// <summary>
/// The design has no anti-spoofing, so a synthetic or cloned voice that reads the phrase is a documented limit, not a
/// tested defence (M7 T7.1). These tests fail if the documents stop saying so, or start claiming protection that no
/// test proves.
/// </summary>
public sealed class DocumentedLimitsTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VoiceStepUp.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir.FullName, .. parts]));
    }

    [Fact]
    public void SyntheticVoice_IsStatedInTheThreatModelAsUnmitigated()
    {
        var threatModel = RepoFile("docs", "threat-model.md");
        var row = threatModel.Split('\n').Single(line =>
            line.StartsWith("| TM-002 |", StringComparison.Ordinal) && line.Contains("Synthetic", StringComparison.Ordinal));

        Assert.Contains("no anti-spoofing", row, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No countermeasure", row, StringComparison.Ordinal);
    }

    [Fact]
    public void SyntheticVoice_IsStatedInTheEvaluationReportAsNotMeasured()
    {
        var report = RepoFile("docs", "eval-report.md");
        var notMeasured = report[report.IndexOf("## Not measured", StringComparison.Ordinal)..];

        Assert.Contains("synthetic or cloned voices", notMeasured, StringComparison.Ordinal);
    }

    [Fact]
    public void Documents_DoNotClaimLivenessOrAntiSpoofingProtection()
    {
        foreach (var file in new[] { new[] { "docs", "architecture.md" }, ["docs", "adr", "0002-phrase-and-asr-liveness.md"] })
        {
            var text = RepoFile(file);
            // "no anti-spoofing" and "not liveness" are the honest statements; a bare claim would be the overclaim.
            foreach (var claim in new[] { "provides liveness", "is live", "anti-spoofing protection", "replay-proof", "deepfake-resistant" })
            {
                // Whole words only, so "this live demo" does not trip "is live".
                Assert.DoesNotMatch($@"(?i)\b{Regex.Escape(claim)}\b", text);
            }
        }
    }
}
