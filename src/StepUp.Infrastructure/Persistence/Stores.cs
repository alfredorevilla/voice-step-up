using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepUp.Core;
using StepUp.Infrastructure.Security;

namespace StepUp.Infrastructure.Persistence;

internal static class Ms
{
    public static long Of(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    public static DateTimeOffset To(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);
}

public sealed class ChallengeStore(IDbContextFactory<StepUpDbContext> factory) : IChallengeStore
{
    public async Task<int> CountOpenAsync(UserKey user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var nowMs = Ms.Of(now);
        return await db.Challenges.CountAsync(c => c.UserKey == user.Value && c.ConsumedAt == null && c.ExpiresAt > nowMs, cancellationToken);
    }

    public async Task AddAsync(Challenge challenge, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Challenges.Add(new ChallengeRow
        {
            Id = challenge.Id,
            UserKey = challenge.User.Value,
            Action = challenge.Action,
            Phrase = challenge.Phrase,
            ExpiresAt = Ms.Of(challenge.ExpiresAt),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Challenge?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Challenges.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return row is null
            ? null
            : new Challenge(row.Id, new UserKey(row.UserKey), row.Action, row.Phrase, Ms.To(row.ExpiresAt), row.ConsumedAt is { } c ? Ms.To(c) : null);
    }

    public async Task<string?> TryConsumeAsync(Guid id, UserKey user, string action, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var nowMs = Ms.Of(now);
        var phrase = await db.Challenges.Where(c => c.Id == id).Select(c => c.Phrase).FirstOrDefaultAsync(cancellationToken);

        // One conditional UPDATE: only a single concurrent caller sees an affected row.
        var updated = await db.Challenges
            .Where(c => c.Id == id && c.UserKey == user.Value && c.Action == action && c.ConsumedAt == null && c.ExpiresAt > nowMs)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Phrase, (string?)null).SetProperty(c => c.ConsumedAt, nowMs), cancellationToken);
        return updated == 1 ? phrase : null;
    }
}

public sealed class VoiceprintStore(IDbContextFactory<StepUpDbContext> factory, EmbeddingProtector protector) : IVoiceprintStore
{
    public async Task<Voiceprint?> GetAsync(UserKey user, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Voiceprints.AsNoTracking().FirstOrDefaultAsync(v => v.UserKey == user.Value, cancellationToken);
        return row is null ? null : new Voiceprint(user, row.ModelId, protector.Unprotect(row.EmbeddingProtected), Ms.To(row.CreatedAt));
    }

    public async Task ReplaceAsync(Voiceprint voiceprint, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Voiceprints.Where(v => v.UserKey == voiceprint.User.Value).ExecuteDeleteAsync(cancellationToken);
        db.Voiceprints.Add(new VoiceprintRow
        {
            Id = Guid.NewGuid(),
            UserKey = voiceprint.User.Value,
            ModelId = voiceprint.ModelId,
            EmbeddingProtected = protector.Protect(voiceprint.Embedding),
            CreatedAt = Ms.Of(voiceprint.CreatedAt),
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(UserKey user, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Voiceprints.Where(v => v.UserKey == user.Value).ExecuteDeleteAsync(cancellationToken) > 0;
    }
}

public sealed class ConsentStore(IDbContextFactory<StepUpDbContext> factory, TimeProvider time) : IConsentStore
{
    public async Task<bool> IsActiveAsync(UserKey user, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Consents.AnyAsync(c => c.UserKey == user.Value && c.WithdrawnAt == null, cancellationToken);
    }

    public async Task<bool> GiveAsync(UserKey user, string textVersion, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.Consents.AnyAsync(c => c.UserKey == user.Value && c.WithdrawnAt == null, cancellationToken))
        {
            return false;
        }

        db.Consents.Add(new ConsentRow { Id = Guid.NewGuid(), UserKey = user.Value, TextVersion = textVersion, GivenAt = Ms.Of(time.GetUtcNow()) });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task WithdrawAsync(UserKey user, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var now = Ms.Of(time.GetUtcNow());
        await db.Consents
            .Where(c => c.UserKey == user.Value && c.WithdrawnAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, now), cancellationToken);
    }
}

public sealed class AttemptLog(IDbContextFactory<StepUpDbContext> factory) : IAttemptLog
{
    public async Task AppendAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Attempts.Add(new AttemptRow
        {
            Id = attempt.Id,
            UserKey = attempt.User.Value,
            At = Ms.Of(attempt.At),
            Action = attempt.Action,
            Score = attempt.Score,
            Threshold = attempt.Threshold,
            ModelId = attempt.ModelId,
            Result = (int)attempt.Result,
            ReasonCode = attempt.Reason is { } r ? (int)r : null,
            Mode = (int)attempt.Mode,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Attempt>> ListAsync(AttemptFilter filter, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.Attempts.AsNoTracking().AsQueryable();
        if (filter.From is { } from)
        {
            var ms = Ms.Of(from);
            query = query.Where(a => a.At >= ms);
        }

        if (filter.To is { } to)
        {
            var ms = Ms.Of(to);
            query = query.Where(a => a.At <= ms);
        }

        if (filter.Result is { } result)
        {
            var value = (int)result;
            query = query.Where(a => a.Result == value);
        }

        var rows = await query.OrderByDescending(a => a.At).Skip(filter.Skip).Take(filter.Take).ToListAsync(cancellationToken);
        return [.. rows.Select(a => new Attempt(
            a.Id, new UserKey(a.UserKey), Ms.To(a.At), a.Action, a.Score, a.Threshold, a.ModelId,
            (AttemptResult)a.Result, a.ReasonCode is { } rc ? (ReasonCode)rc : null, (AttemptMode)a.Mode))];
    }

    public async Task<VoiceFailureState> GetFailureStateAsync(UserKey user, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var since = Ms.Of(notBefore);
        var voice = (int)AttemptMode.Voice;
        var decided = new[] { (int)AttemptResult.Pass, (int)AttemptResult.Fail };
        var recent = await db.Attempts.AsNoTracking()
            .Where(a => a.UserKey == user.Value && a.Mode == voice && a.At >= since && decided.Contains(a.Result))
            .OrderByDescending(a => a.At)
            .Select(a => new { a.Result, a.At })
            .ToListAsync(cancellationToken);

        var failures = recent.TakeWhile(a => a.Result == (int)AttemptResult.Fail).ToList();
        return new VoiceFailureState(failures.Count, failures.Count == 0 ? null : Ms.To(failures[0].At));
    }
}

public sealed class ThresholdStore(IDbContextFactory<StepUpDbContext> factory) : IThresholdStore
{
    public async Task<ThresholdEntry?> GetActiveAsync(string name, string modelId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Thresholds.AsNoTracking()
            .Where(t => t.Name == name && t.ModelId == modelId)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToEntry(row);
    }

    public async Task SetAsync(ThresholdEntry entry, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Thresholds.Add(new ThresholdRow
        {
            Name = entry.Name,
            ModelId = entry.ModelId,
            Value = entry.Value,
            UpdatedAt = Ms.Of(entry.UpdatedAt),
            UpdatedBy = entry.UpdatedBy,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ThresholdEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Thresholds.AsNoTracking().OrderByDescending(t => t.Id).ToListAsync(cancellationToken);
        return [.. rows.Select(ToEntry)];
    }

    private static ThresholdEntry ToEntry(ThresholdRow r) => new(r.Name, r.ModelId, r.Value, Ms.To(r.UpdatedAt), r.UpdatedBy);
}

public sealed class EnrollmentStore(IDbContextFactory<StepUpDbContext> factory, EmbeddingProtector protector) : IEnrollmentStore
{
    private sealed record Payload(string[] Phrases, float[][] Samples);

    public async Task SaveAsync(EnrollmentState state, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload([.. state.Phrases], [.. state.Samples]));
        var protectedBytes = protector.ProtectBytes(payload);
        var existing = await db.Enrollments.FirstOrDefaultAsync(e => e.Id == state.Id, cancellationToken);
        if (existing is null)
        {
            db.Enrollments.Add(new EnrollmentRow { Id = state.Id, UserKey = state.User.Value, SamplesProtected = protectedBytes, ExpiresAt = Ms.Of(state.ExpiresAt) });
        }
        else
        {
            existing.SamplesProtected = protectedBytes;
            existing.ExpiresAt = Ms.Of(state.ExpiresAt);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<EnrollmentState?> GetAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var nowMs = Ms.Of(now);
        var row = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        if (row.ExpiresAt <= nowMs)
        {
            // An expired enrollment still holds sample embeddings: remove it when it is touched.
            await db.Enrollments.Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        var payload = JsonSerializer.Deserialize<Payload>(protector.UnprotectBytes(row.SamplesProtected))!;
        return new EnrollmentState(row.Id, new UserKey(row.UserKey), payload.Phrases, payload.Samples, Ms.To(row.ExpiresAt));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Enrollments.Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteForUserAsync(UserKey user, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Enrollments.Where(e => e.UserKey == user.Value).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var nowMs = Ms.Of(now);
        await db.Enrollments.Where(e => e.ExpiresAt <= nowMs).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed class UsedAssertionStore(IDbContextFactory<StepUpDbContext> factory) : IUsedAssertionStore
{
    public async Task<bool> TryUseAsync(string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var expires = Ms.Of(expiresAt);

        // One atomic statement: exactly one caller inserts the jti, a replay inserts nothing and raises no exception.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT OR IGNORE INTO UsedAssertions (Jti, ExpiresAt) VALUES ({jti}, {expires})", cancellationToken);
        return inserted == 1;
    }
}
