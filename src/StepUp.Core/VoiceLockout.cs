namespace StepUp.Core;

public sealed record LockoutOptions
{
    public int DelayAfterFailures { get; init; } = 5;

    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(30);

    public int DisableAfterFailures { get; init; } = 50;
}

public enum LockoutKind { None, Delayed, Disabled }

public sealed record LockoutState(LockoutKind Kind, TimeSpan? RetryAfter);

/// <summary>NIST SP 800-63B-4 3.2.3.3 limits for a system without presentation attack detection.</summary>
public static class VoiceLockout
{
    public static LockoutState Evaluate(VoiceFailureState state, LockoutOptions options, DateTimeOffset now)
    {
        if (state.ConsecutiveFailures >= options.DisableAfterFailures)
        {
            return new LockoutState(LockoutKind.Disabled, null);
        }

        if (state.ConsecutiveFailures >= options.DelayAfterFailures && state.LastFailureAt is { } last)
        {
            var remaining = last + options.Delay - now;
            if (remaining > TimeSpan.Zero)
            {
                return new LockoutState(LockoutKind.Delayed, remaining);
            }
        }

        return new LockoutState(LockoutKind.None, null);
    }
}
