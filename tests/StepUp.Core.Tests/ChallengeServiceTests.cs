using Microsoft.Extensions.Time.Testing;
using StepUp.Core;

namespace StepUp.Core.Tests;

public class ChallengeServiceTests
{
    private static readonly UserKey Alice = new("alice");
    private static readonly UserKey Bob = new("bob");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryChallengeStore _store = new();

    private ChallengeService Create(ChallengeOptions? options = null) => new(_store, options ?? new ChallengeOptions(), _time);

    [Fact]
    public async Task Challenge_HasFourWordPhrase()
    {
        var result = await Create().IssueAsync(Alice, "payout", TestContext.Current.CancellationToken);

        Assert.NotNull(result.Challenge);
        Assert.Equal(4, result.Challenge.Phrase!.Split(' ').Length);
    }

    [Fact]
    public async Task Challenge_IsSingleUse()
    {
        var service = Create();
        var challenge = (await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken)).Challenge!;

        var first = await service.ConsumeAsync(Alice, challenge.Id, "payout", TestContext.Current.CancellationToken);
        var second = await service.ConsumeAsync(Alice, challenge.Id, "payout", TestContext.Current.CancellationToken);

        Assert.Equal(challenge.Phrase, first.Phrase);
        Assert.Equal(ReasonCode.ChallengeConsumed, second.Reason);
    }

    [Fact]
    public async Task Challenge_PhraseIsNulledAfterConsume()
    {
        var service = Create();
        var challenge = (await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken)).Challenge!;

        await service.ConsumeAsync(Alice, challenge.Id, "payout", TestContext.Current.CancellationToken);

        Assert.Null((await _store.GetAsync(challenge.Id, TestContext.Current.CancellationToken))!.Phrase);
    }

    [Fact]
    public async Task Challenge_ExpiresAfter60s()
    {
        var service = Create(new ChallengeOptions { Ttl = TimeSpan.FromSeconds(60) });
        var challenge = (await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken)).Challenge!;

        _time.Advance(TimeSpan.FromSeconds(61));
        var result = await service.ConsumeAsync(Alice, challenge.Id, "payout", TestContext.Current.CancellationToken);

        Assert.Equal(ReasonCode.ChallengeExpired, result.Reason);
    }

    [Fact]
    public async Task Challenge_RejectsOtherUser()
    {
        var service = Create();
        var challenge = (await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken)).Challenge!;

        var result = await service.ConsumeAsync(Bob, challenge.Id, "payout", TestContext.Current.CancellationToken);

        Assert.Equal(ReasonCode.ChallengeExpired, result.Reason);
        Assert.NotNull((await service.ValidateAsync(Alice, challenge.Id, "payout", TestContext.Current.CancellationToken)).Phrase);
    }

    [Fact]
    public async Task Challenge_RejectsOtherAction()
    {
        var service = Create();
        var challenge = (await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken)).Challenge!;

        var result = await service.ConsumeAsync(Alice, challenge.Id, "delete-account", TestContext.Current.CancellationToken);

        Assert.Equal(ReasonCode.ChallengeExpired, result.Reason);
    }

    [Fact]
    public async Task Challenge_ConcurrentVerify_OnlyOneSucceeds()
    {
        var service = Create();
        var challenge = (await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken)).Challenge!;

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => service.ConsumeAsync(Alice, challenge.Id, "payout", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.Single(results, r => r.Phrase is not null);
    }

    [Fact]
    public async Task Challenge_CapsOpenChallengesPerUser()
    {
        var service = Create(new ChallengeOptions { MaxOpenPerUser = 2 });
        await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken);
        await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken);

        var third = await service.IssueAsync(Alice, "payout", TestContext.Current.CancellationToken);
        var other = await service.IssueAsync(Bob, "payout", TestContext.Current.CancellationToken);

        Assert.Equal(ReasonCode.RateLimited, third.Reason);
        Assert.NotNull(other.Challenge);
    }

    [Fact]
    public void Phrase_UsesCsprngWordsAndIsUsuallyDifferent()
    {
        var phrases = Enumerable.Range(0, 50).Select(_ => PhraseGenerator.Create()).ToHashSet();

        Assert.True(phrases.Count > 45);
    }

    [Theory]
    [InlineData("Apple, ANCHOR arrow autumn.", "apple anchor arrow autumn", true)]
    [InlineData("Lift each clock turtle.", "cliff peach clock turtle", true)] // ASR splits and misspells isolated nouns
    [InlineData("banana dragon whistle quilt", "apple anchor arrow autumn", false)]
    [InlineData("bamboo basket beacon bridge", "apple anchor arrow autumn", false)]
    [InlineData("[ Silence ]", "apple anchor arrow autumn", false)]
    [InlineData("(Bell)", "apple anchor arrow autumn", false)]
    [InlineData("", "apple anchor arrow autumn", false)]
    [InlineData(null, "apple anchor arrow autumn", false)]
    public void Phrase_MatchToleratesAsrSpellingButNotAnotherPhrase(string? transcript, string phrase, bool expected)
    {
        Assert.Equal(expected, PhraseGenerator.Matches(transcript, phrase));
    }

    [Fact]
    public void Phrase_SimilarityIsLetterBasedAndIgnoresNonSpeechTags()
    {
        Assert.Equal(1.0, PhraseGenerator.Similarity("Apple Anchor", "apple  anchor"), 5);
        Assert.Equal(1.0, PhraseGenerator.Similarity("[ Silence ] apple anchor", "apple anchor"), 5);
        Assert.Equal(0.0, PhraseGenerator.Similarity("(Bell)", "apple anchor"), 5);
        Assert.InRange(PhraseGenerator.Similarity("zzzz", "apple anchor"), 0.0, 0.2);
    }

    [Fact]
    public void Phrase_MatchThresholdIs060()
    {
        Assert.Equal(0.60, PhraseGenerator.MatchThreshold);
    }
}
