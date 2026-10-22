using System.Globalization;
using StepUp.Api.Flow;
using StepUp.Core;

namespace StepUp.Api.Endpoints;

public sealed record ConsentRequest(string TextVersion);

public sealed record ChallengeRequest(string Action);

public sealed record ThresholdRequest(string Name, double Value);

public sealed record PayoutRequest(decimal Amount, string To) : IStepUpPayload
{
    // Dividing by 1.000...0m (28 decimals, the decimal maximum) drops trailing zeros, so "10.0" at verify and 10 at payout hash the same.
    private const decimal DropTrailingZeros = 1.0000000000000000000000000000m;

    public string PayloadHash => StepUpAssertionService.PayloadHash((Amount / DropTrailingZeros).ToString(CultureInfo.InvariantCulture), To);
}
