using StepUp.Core;
using StepUp.Infrastructure.Verification;

namespace StepUp.Verifier.Tests;

public class FakeVerifierContractTests : VerifierContract
{
    protected override IVerifier Create() => new FakeVerifier();

    protected override byte[] Speech(int speaker, string phrase) => FakeAudio.Create((byte)speaker, phrase);

    protected override byte[] MutedAudio() => FakeAudio.Create(1, Phrase, FakeQuality.Muted);

    protected override byte[] TooShortAudio() => FakeAudio.Create(1, Phrase, FakeQuality.TooShort);
}
