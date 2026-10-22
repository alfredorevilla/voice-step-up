using StepUp.Core;

namespace StepUp.Core.Tests;

internal sealed class InMemoryChallengeStore : IChallengeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Challenge> _items = [];

    public Task<int> CountOpenAsync(UserKey user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_items.Values.Count(c => c.User == user && c.ConsumedAt is null && c.ExpiresAt > now));
        }
    }

    public Task AddAsync(Challenge challenge, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _items[challenge.Id] = challenge;
        }

        return Task.CompletedTask;
    }

    public Task<Challenge?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_items.GetValueOrDefault(id));
        }
    }

    public Task<string?> TryConsumeAsync(Guid id, UserKey user, string action, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(id, out var c) && c.User == user && c.Action == action && c.ConsumedAt is null && c.ExpiresAt > now)
            {
                _items[id] = c with { Phrase = null, ConsumedAt = now };
                return Task.FromResult<string?>(c.Phrase);
            }

            return Task.FromResult<string?>(null);
        }
    }
}

internal sealed class InMemoryUsedAssertionStore : IUsedAssertionStore
{
    private readonly HashSet<string> _used = [];

    public Task<bool> TryUseAsync(string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        lock (_used)
        {
            return Task.FromResult(_used.Add(jti));
        }
    }
}
