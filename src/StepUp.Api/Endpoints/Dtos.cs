using System.Globalization;
using StepUp.Api.Flow;
using StepUp.Core;

namespace StepUp.Api.Endpoints;

public sealed record ConsentRequest(string TextVersion);

public sealed record ChallengeRequest(string Action);

public sealed record ThresholdRequest(string Name, double Value);

public sealed record PayoutRequest(decimal Amount, string To) : IStepUpPayload
{
    public string PayloadHash => StepUpAssertionService.PayloadHash(Amount.ToString(CultureInfo.InvariantCulture), To);
}
