using StepUp.Core;

namespace StepUp.Verifier.Tests;

/// <summary>
/// Behaviour every <see cref="IVerifier"/> must show. A new implementation is added behind this suite
/// (derive from it and supply real or fake audio) before any wiring. Verifiers extract; they never score or decide.
/// </summary>
public abstract class VerifierContract
{
    protected const string Phrase = "apple anchor arrow autumn";

    protected abstract IVerifier Create();

    /// <summary>Audio of a clean utterance of <paramref name="phrase"/> by the given speaker.</summary>
    protected abstract byte[] Speech(int speaker, string phrase);

    protected abstract byte[] MutedAudio();

    protected abstract byte[] TooShortAudio();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ModelId_IsStableAndNotEmpty()
    {
        var verifier = Create();

        Assert.False(string.IsNullOrWhiteSpace(verifier.ModelId));
        Assert.Equal(verifier.ModelId, Create().ModelId);
    }

    [Fact]
    public async Task Analyze_ReturnsEmbeddingAndTranscript_ForCleanSpeech()
    {
        var result = await Create().AnalyzeAsync(Speech(1, Phrase), Phrase, Ct);

        Assert.Null(result.Reject);
        Assert.NotNull(result.Embedding);
        Assert.NotEmpty(result.Embedding);
        Assert.True(PhraseGenerator.Matches(result.Transcript, Phrase), $"heard '{result.Transcript}'");
    }

    [Fact]
    public async Task Analyze_EmbeddingsKeepTheSameLengthAndAreFinite()
    {
        var verifier = Create();
        var a = (await verifier.AnalyzeAsync(Speech(1, Phrase), Phrase, Ct)).Embedding!;
        var b = (await verifier.AnalyzeAsync(Speech(2, Phrase), Phrase, Ct)).Embedding!;

        Assert.Equal(a.Length, b.Length);
        Assert.All(a, x => Assert.True(float.IsFinite(x)));
    }

    [Fact]
    public async Task Analyze_SameSpeakerScoresHigherThanDifferentSpeaker()
    {
        var verifier = Create();
        var enrolled = (await verifier.AnalyzeAsync(Speech(1, "bamboo basket beacon bridge"), "bamboo basket beacon bridge", Ct)).Embedding!;
        var same = (await verifier.AnalyzeAsync(Speech(1, Phrase), Phrase, Ct)).Embedding!;
        var other = (await verifier.AnalyzeAsync(Speech(2, Phrase), Phrase, Ct)).Embedding!;

        Assert.True(Cosine.Similarity(enrolled, same) > Cosine.Similarity(enrolled, other));
    }

    [Fact]
    public async Task Analyze_ReportsTheSpokenWords_NotTheExpectedOnes()
    {
        var result = await Create().AnalyzeAsync(Speech(1, "bamboo basket beacon bridge"), Phrase, Ct);

        Assert.False(PhraseGenerator.Matches(result.Transcript, Phrase));
    }

    [Fact]
    public async Task Analyze_RejectsMutedAudio()
    {
        var result = await Create().AnalyzeAsync(MutedAudio(), Phrase, Ct);

        Assert.Equal(ReasonCode.Muted, result.Reject);
        Assert.Null(result.Embedding);
    }

    [Fact]
    public async Task Analyze_RejectsTooShortAudio()
    {
        var result = await Create().AnalyzeAsync(TooShortAudio(), Phrase, Ct);

        Assert.Equal(ReasonCode.TooShort, result.Reject);
        Assert.Null(result.Embedding);
    }

    [Fact]
    public async Task Analyze_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().AnalyzeAsync(Speech(1, Phrase), Phrase, cts.Token));
    }

    [Fact]
    public async Task Analyze_IsDeterministicForTheSameAudio()
    {
        var verifier = Create();
        var audio = Speech(3, Phrase);

        var first = (await verifier.AnalyzeAsync(audio, Phrase, Ct)).Embedding!;
        var second = (await verifier.AnalyzeAsync(audio, Phrase, Ct)).Embedding!;

        Assert.Equal(first, second);
    }
}
