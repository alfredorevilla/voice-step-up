namespace StepUp.Core;

public sealed record FallbackOptions
{
    public TimeSpan MaxAuthAge { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// ADR-0004 rule: amr has mfa or ngcmfa and auth_time is within the window. iat is never a proxy.
/// UNVERIFIED against a live tenant until M5 T5.4; keep the rule behind <see cref="IFallbackFactor"/>.
/// </summary>
public sealed class FreshMfaFallback(FallbackOptions options, TimeProvider time) : IFallbackFactor
{
    public FallbackResult Evaluate(Caller caller)
    {
        if (!caller.Amr.Any(a => a is "mfa" or "ngcmfa"))
        {
            return new FallbackResult(false, "no mfa in amr");
        }

        if (caller.AuthTime is null)
        {
            return new FallbackResult(false, "auth_time missing");
        }

        var age = time.GetUtcNow() - caller.AuthTime.Value;
        return age < TimeSpan.Zero || age > options.MaxAuthAge
            ? new FallbackResult(false, "auth_time outside window")
            : new FallbackResult(true, "fresh mfa");
    }
}

public sealed class StepUpPolicy(IFallbackFactor fallback) : IStepUpPolicy
{
    public bool IsSatisfied(Caller caller, bool assertionValid) => assertionValid || fallback.Evaluate(caller).Fresh;
}
