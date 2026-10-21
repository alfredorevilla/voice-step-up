using System.Globalization;
using System.Text.Json;
using StepUp.Api.Flow;

namespace StepUp.Api.Tests;

/// <summary>
/// The operating values the product ships must be the ones the evaluation froze on dev-clean (docs/eval-report.md rules R1 and R3),
/// in the code defaults, in appsettings.json and in docs/eval/summary.csv. If an evaluation run changes them, change all three.
/// </summary>
public class ShippedDefaultsTests
{
    // The evaluation condition the product's probe length matches (docs/design/evaluation-protocol.md, conditions table).
    private const string ShippedCondition = "P0";

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VoiceStepUp.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
    }

    private static double[] DevDefaultThresholdFromTheReport()
    {
        var rows = File.ReadLines(RepoFile("docs", "eval", "summary.csv")).Skip(1).Select(l => l.Split(','));
        return [.. rows
            .Where(r => r[0] == "dev-clean" && r[1] == ShippedCondition && r[2] == "default")
            .Select(r => double.Parse(r[3], CultureInfo.InvariantCulture))];
    }

    [Fact]
    public void Code_defaults_match_the_frozen_evaluation_values()
    {
        var options = new StepUpOptions();

        Assert.Equal(0.79, options.ThresholdSeed);
        Assert.Equal(0.46, options.EnrollmentConsistencyFloor);
    }

    [Fact]
    public void Appsettings_matches_the_code_defaults()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoFile("src", "StepUp.Api", "appsettings.json")));
        var section = doc.RootElement.GetProperty("StepUp");
        var options = new StepUpOptions();

        Assert.Equal(options.ThresholdSeed, section.GetProperty("ThresholdSeed").GetDouble());
        Assert.Equal(options.EnrollmentConsistencyFloor, section.GetProperty("EnrollmentConsistencyFloor").GetDouble());
    }

    [Fact]
    public void Threshold_default_is_the_dev_clean_default_threshold_in_the_published_results()
    {
        var published = DevDefaultThresholdFromTheReport();

        Assert.Single(published);
        Assert.Equal(published[0], new StepUpOptions().ThresholdSeed);
    }
}
