using Microsoft.Extensions.Options;
using StepUp.Core;
using StepUp.Verifier.Sherpa;

namespace StepUp.Verifier.Tests;

/// <summary>The shared contract against the real models. Needs scripts/fetch-models.sh --test.</summary>
[Trait("Category", "Model")]
public class SherpaVerifierContractTests : VerifierContract
{
    private static readonly Lazy<SherpaVerifier> Shared = new(() => new SherpaVerifier(Options.Create(new SherpaVerifierOptions { ModelsDir = ModelAssets.ModelsDir })));

    protected override IVerifier Create() => Shared.Value;

    protected override byte[] Speech(int speaker, string phrase) => ModelAssets.Speech(speaker, phrase);

    protected override byte[] MutedAudio() => ModelAssets.Silence(2);

    protected override byte[] TooShortAudio() => ModelAssets.Truncate(ModelAssets.Speech(1, "apple"), 0.3);
}
