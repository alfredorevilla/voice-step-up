using Microsoft.Extensions.Time.Testing;
using StepUp.Core;

namespace StepUp.Core.Tests;

public class FallbackAndLockoutTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    private FreshMfaFallback Fallback => new(new FallbackOptions { MaxAuthAge = TimeSpan.FromMinutes(10) }, _time);

    private Caller CallerWith(string[] amr, TimeSpan? authAge) =>
        new("tid", "oid", amr, authAge is null ? null : _time.GetUtcNow() - authAge.Value);

    [Theory]
    [InlineData("mfa")]
    [InlineData("ngcmfa")]
    public void Fallback_AcceptsFreshMfa(string amr)
    {
        Assert.True(Fallback.Evaluate(CallerWith(["pwd", amr], TimeSpan.FromMinutes(2))).Fresh);
    }

    [Fact]
    public void Fallback_RejectsStaleAuthTime()
    {
        Assert.False(Fallback.Evaluate(CallerWith(["mfa"], TimeSpan.FromMinutes(11))).Fresh);
    }

    [Fact]
    public void Fallback_RejectsMissingAuthTime()
    {
        Assert.False(Fallback.Evaluate(CallerWith(["mfa"], null)).Fresh);
    }

    [Fact]
    public void Fallback_RejectsAmrWithoutMfa()
    {
        Assert.False(Fallback.Evaluate(CallerWith(["pwd"], TimeSpan.FromMinutes(1))).Fresh);
    }

    [Fact]
    public void Fallback_RejectsAuthTimeInTheFuture()
    {
        Assert.False(Fallback.Evaluate(CallerWith(["mfa"], TimeSpan.FromMinutes(-5))).Fresh);
    }

    [Fact]
    public void Policy_IsSatisfiedByAssertionOrFreshMfaOnly()
    {
        var policy = new StepUpPolicy(Fallback);
        var stale = CallerWith(["mfa"], TimeSpan.FromHours(1));

        Assert.True(policy.IsSatisfied(stale, assertionValid: true));
        Assert.False(policy.IsSatisfied(stale, assertionValid: false));
        Assert.True(policy.IsSatisfied(CallerWith(["mfa"], TimeSpan.FromMinutes(1)), assertionValid: false));
    }

    [Fact]
    public void Attempts_NoDelayBeforeFiveFailures()
    {
        var state = VoiceLockout.Evaluate(new VoiceFailureState(4, _time.GetUtcNow()), new LockoutOptions(), _time.GetUtcNow());

        Assert.Equal(LockoutKind.None, state.Kind);
    }

    [Fact]
    public void Attempts_DelayAfter5()
    {
        var state = VoiceLockout.Evaluate(new VoiceFailureState(5, _time.GetUtcNow() - TimeSpan.FromSeconds(10)), new LockoutOptions(), _time.GetUtcNow());

        Assert.Equal(LockoutKind.Delayed, state.Kind);
        Assert.Equal(TimeSpan.FromSeconds(20), state.RetryAfter);
    }

    [Fact]
    public void Attempts_DelayEndsAfter30Seconds()
    {
        var state = VoiceLockout.Evaluate(new VoiceFailureState(7, _time.GetUtcNow() - TimeSpan.FromSeconds(31)), new LockoutOptions(), _time.GetUtcNow());

        Assert.Equal(LockoutKind.None, state.Kind);
    }

    [Fact]
    public void Attempts_DisableAfter50()
    {
        var state = VoiceLockout.Evaluate(new VoiceFailureState(50, _time.GetUtcNow() - TimeSpan.FromHours(1)), new LockoutOptions(), _time.GetUtcNow());

        Assert.Equal(LockoutKind.Disabled, state.Kind);
    }
}
