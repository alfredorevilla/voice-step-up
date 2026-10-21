using System.Globalization;
using System.Text.Json;
using StepUp.Api.Flow;
using StepUp.Core;

namespace StepUp.Api.Tests;

/// <summary>
/// The operating values the product ships must be the ones the evaluation froze on dev-clean (docs/eval-report.md rules R1 and R3),
/// in the code defaults, in appsettings.json and in docs/eval/summary.csv. If an evaluation run changes them, change all three.
/// </summary>
public class ShippedDefaultsTests
{
    // The evaluation condition the product's challenge matches: 6 words, a probe of about 3 s with a 3.5 s enrollment
    // (docs/design/evaluation-protocol.md, Amendment 2, conditions table; adopted by rule R6 in docs/eval-report.md).
    private const string ShippedCondition = "L3E";

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

        Assert.Equal(0.80, options.ThresholdSeed);
        Assert.Equal(0.43, options.EnrollmentConsistencyFloor);
    }

    [Fact]
    public void Challenge_and_enrollment_phrases_have_the_six_words_the_evaluation_adopted()
    {
        Assert.Equal(6, PhraseGenerator.WordsPerPhrase);
        Assert.Equal(6, PhraseGenerator.Create().Split(' ').Length);
        Assert.Equal(6, PhraseGenerator.CreateEnrollment().Split(' ').Length);
    }

    [Fact]
    public void The_per_word_phrase_rule_stays_off_because_no_floor_met_its_gate()
    {
        Assert.Equal(0, new StepUpOptions().PhraseMinWordSimilarity);
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
