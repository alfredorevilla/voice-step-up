using StepUp.Api.Endpoints;

namespace StepUp.Api.Tests;

/// <summary>The payout hash must not depend on how many trailing zeros the amount was written with.</summary>
public class PayoutPayloadHashTests
{
    [Theory]
    [InlineData("10", "10.0")]
    [InlineData("10.5", "10.50")]
    [InlineData("1000", "1000.00")]
    public void SameAmountWithDifferentScaleHashesTheSame(string a, string b)
    {
        Assert.Equal(
            new PayoutRequest(decimal.Parse(a, System.Globalization.CultureInfo.InvariantCulture), "acct").PayloadHash,
            new PayoutRequest(decimal.Parse(b, System.Globalization.CultureInfo.InvariantCulture), "acct").PayloadHash);
    }

    [Fact]
    public void DifferentAmountsOrRecipientsHashDifferently()
    {
        var baseHash = new PayoutRequest(10m, "acct").PayloadHash;
        Assert.NotEqual(baseHash, new PayoutRequest(10.01m, "acct").PayloadHash);
        Assert.NotEqual(baseHash, new PayoutRequest(10m, "other").PayloadHash);
    }

    [Fact]
    public void ExtremeAmountsDoNotOverflow()
    {
        Assert.NotEmpty(new PayoutRequest(decimal.MaxValue, "acct").PayloadHash);
        Assert.NotEmpty(new PayoutRequest(0.0000000000000000000000000001m, "acct").PayloadHash);
    }
}
