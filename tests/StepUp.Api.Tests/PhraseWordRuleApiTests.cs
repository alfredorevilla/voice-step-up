using System.Net;
using System.Net.Http.Json;
using StepUp.Core;

namespace StepUp.Api.Tests;

/// <summary>
/// The per-word phrase rule is a configuration switch (StepUp:PhraseMinWordSimilarity). Off, a phrase with one wrong word passes
/// (the documented limit of docs/adr/0002); on, it is refused with PhraseMismatch.
/// </summary>
public sealed class PhraseWordRuleApiTests : IDisposable
{
    private readonly ApiFactory _off = new();
    private readonly ApiFactory _on = new(config: new Dictionary<string, string?> { ["StepUp:PhraseMinWordSimilarity"] = "0.5" });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _off.Dispose();
        _on.Dispose();
    }

    private static async Task<(HttpClient Client, Guid Id, string[] Phrases)> StartedAsync(ApiFactory api)
    {
        var client = api.ClientFor(api.FreshMfa(Users.Alice));
        (await client.PostAsJsonAsync("/api/enrollment/consent", new { textVersion = "v1" }, Ct)).EnsureSuccessStatusCode();
        var body = await (await client.PostAsync("/api/enrollment/start", null, Ct)).JsonAsync();
        return (client, body.GetProperty("enrollmentId").GetGuid(), [.. body.GetProperty("phrases").EnumerateArray().Select(p => p.GetString()!)]);
    }

    // Replace the last word with letters that never occur in it: one wrong word out of four.
    private static string OneWrongWord(string phrase)
    {
        var words = phrase.Split(' ');
        words[^1] = new string('q', words[^1].Length);
        var near = string.Join(' ', words);
        Assert.True(PhraseGenerator.Matches(near, phrase), "premise: the whole-phrase rule accepts the near miss");
        return near;
    }

    [Fact]
    public async Task With_the_word_rule_off_one_wrong_word_is_accepted_as_a_documented_limit()
    {
        var (client, id, phrases) = await StartedAsync(_off);
        using var _c = client;

        var sample = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(1, OneWrongWord(phrases[0]))), Ct);

        Assert.Equal(HttpStatusCode.OK, sample.StatusCode);
    }

    [Fact]
    public async Task With_the_word_rule_on_one_wrong_word_is_refused_and_the_exact_phrase_is_accepted()
    {
        var (client, id, phrases) = await StartedAsync(_on);
        using var _c = client;

        var near = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(1, OneWrongWord(phrases[0]))), Ct);
        var exact = await client.PostAsync($"/api/enrollment/sample?enrollmentId={id}", HttpExtensions.Wav(ApiFactory.Speech(1, phrases[0])), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, near.StatusCode);
        Assert.Equal("PhraseMismatch", await near.CodeAsync());
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }
}
