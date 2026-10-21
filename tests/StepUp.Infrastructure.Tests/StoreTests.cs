using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StepUp.Core;
using StepUp.Infrastructure.Persistence;
using StepUp.Infrastructure.Security;

namespace StepUp.Infrastructure.Tests;

public sealed class StoreTests : IDisposable
{
    private static readonly UserKey Alice = new("alice");

    private readonly TestDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Voiceprint_RoundTripsAndIsEncryptedAtRest()
    {
        float[] embedding = [0.25f, -0.5f, 0.125f, 0.75f];
        await _db.Get<IVoiceprintStore>().ReplaceAsync(new Voiceprint(Alice, "model-1", embedding, _db.Time.GetUtcNow()), Ct);

        var loaded = await _db.Get<IVoiceprintStore>().GetAsync(Alice, Ct);
        await using var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct);
        var raw = (await context.Voiceprints.AsNoTracking().SingleAsync(Ct)).EmbeddingProtected;
        var plain = new byte[embedding.Length * 4];
        Buffer.BlockCopy(embedding, 0, plain, 0, plain.Length);

        Assert.Equal(embedding, loaded!.Embedding);
        Assert.Equal("model-1", loaded.ModelId);
        Assert.False(raw.AsSpan().IndexOf(plain) >= 0, "embedding bytes must not appear in plaintext");
    }

    [Fact]
    public async Task Voiceprint_ReplaceKeepsExactlyOneRow()
    {
        var store = _db.Get<IVoiceprintStore>();
        await store.ReplaceAsync(new Voiceprint(Alice, "m", [1f, 0f], _db.Time.GetUtcNow()), Ct);
        await store.ReplaceAsync(new Voiceprint(Alice, "m", [0f, 1f], _db.Time.GetUtcNow()), Ct);

        await using var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct);

        var rows = await context.Voiceprints.CountAsync(Ct);
        var stored = await store.GetAsync(Alice, Ct);

        Assert.Equal(1, rows);
        Assert.Equal([0f, 1f], stored!.Embedding);
    }

    [Fact]
    public async Task Delete_LeavesNoEmbeddingBytesInDbFile()
    {
        var store = _db.Get<IVoiceprintStore>();
        await store.ReplaceAsync(new Voiceprint(Alice, "m", Enumerable.Range(0, 64).Select(i => (float)Math.Sin(i)).ToArray(), _db.Time.GetUtcNow()), Ct);
        byte[] marker;
        await using (var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct))
        {
            marker = (await context.Voiceprints.AsNoTracking().SingleAsync(Ct)).EmbeddingProtected[..48];
        }

        Assert.True(FileContains(marker), "the marker must be in the file before the delete, or this test proves nothing");

        Assert.True(await store.DeleteAsync(Alice, Ct));
        await using (var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct))
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", Ct);
        }

        Assert.False(FileContains(marker));
    }

    private bool FileContains(byte[] marker) =>
        new[] { _db.DbPath, _db.DbPath + "-wal", _db.DbPath + "-journal" }
            .Where(File.Exists)
            .Any(path =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray().AsSpan().IndexOf(marker) >= 0;
            });

    [Fact]
    public async Task Consent_GiveWithdrawAndKeepsTheRecord()
    {
        var store = _db.Get<IConsentStore>();

        Assert.True(await store.GiveAsync(Alice, "v1", Ct));
        Assert.False(await store.GiveAsync(Alice, "v1", Ct));
        Assert.True(await store.IsActiveAsync(Alice, Ct));

        await store.WithdrawAsync(Alice, Ct);
        await using var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct);

        Assert.False(await store.IsActiveAsync(Alice, Ct));
        Assert.NotNull((await context.Consents.SingleAsync(Ct)).WithdrawnAt);
    }

    [Fact]
    public async Task Challenge_ConcurrentConsume_OnlyOneSucceeds_OnTheRealStore()
    {
        var service = new ChallengeService(_db.Get<IChallengeStore>(), new ChallengeOptions(), _db.Time);
        var challenge = (await service.IssueAsync(Alice, "payout", Ct)).Challenge!;

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => service.ConsumeAsync(Alice, challenge.Id, "payout", Ct), Ct)));

        Assert.Single(results, r => r.Phrase is not null);
        Assert.Null((await _db.Get<IChallengeStore>().GetAsync(challenge.Id, Ct))!.Phrase);
    }

    [Fact]
    public async Task Attempts_HasNoUpdateOrDeletePath()
    {
        var log = _db.Get<IAttemptLog>();
        var attempt = new Attempt(Guid.NewGuid(), Alice, _db.Time.GetUtcNow(), "payout", 0.9, 0.8, "m", AttemptResult.Pass, null, AttemptMode.Voice);
        await log.AppendAsync(attempt, Ct);

        var members = typeof(IAttemptLog).GetMethods().Select(m => m.Name).ToList();
        await using var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct);
        var row = await context.Attempts.SingleAsync(Ct);
        row.Score = 0.1;

        Assert.DoesNotContain(members, n => n.Contains("Update", StringComparison.Ordinal) || n.Contains("Delete", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Attempts_CountConsecutiveFailuresSinceLastPass()
    {
        var log = _db.Get<IAttemptLog>();
        async Task Add(AttemptResult result, AttemptMode mode = AttemptMode.Voice)
        {
            _db.Time.Advance(TimeSpan.FromSeconds(1));
            await log.AppendAsync(new Attempt(Guid.NewGuid(), Alice, _db.Time.GetUtcNow(), "payout", 0.5, 0.8, "m", result, null, mode), Ct);
        }

        var start = _db.Time.GetUtcNow();
        await Add(AttemptResult.Fail);
        await Add(AttemptResult.Pass);
        await Add(AttemptResult.Fail);
        await Add(AttemptResult.Rejected);
        await Add(AttemptResult.Error);
        await Add(AttemptResult.Fail);
        await Add(AttemptResult.Pass, AttemptMode.Fallback);

        var state = await log.GetFailureStateAsync(Alice, start, Ct);

        Assert.Equal(2, state.ConsecutiveFailures);
        Assert.NotNull(state.LastFailureAt);
    }

    [Fact]
    public async Task Attempts_FailuresBeforeTheVoiceprintWasCreatedDoNotCount()
    {
        var log = _db.Get<IAttemptLog>();
        _db.Time.Advance(TimeSpan.FromSeconds(1));
        await log.AppendAsync(new Attempt(Guid.NewGuid(), Alice, _db.Time.GetUtcNow(), "payout", 0.5, 0.8, "m", AttemptResult.Fail, null, AttemptMode.Voice), Ct);
        _db.Time.Advance(TimeSpan.FromSeconds(1));

        var state = await log.GetFailureStateAsync(Alice, _db.Time.GetUtcNow(), Ct);

        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact]
    public async Task Thresholds_AreKeyedByModelIdAndKeepHistory()
    {
        var store = _db.Get<IThresholdStore>();
        await store.SetAsync(new ThresholdEntry("stepup", "m1", 0.7, _db.Time.GetUtcNow(), "seed"), Ct);
        _db.Time.Advance(TimeSpan.FromMinutes(1));
        await store.SetAsync(new ThresholdEntry("stepup", "m1", 0.8, _db.Time.GetUtcNow(), "analyst-1"), Ct);
        await store.SetAsync(new ThresholdEntry("stepup", "m2", 0.5, _db.Time.GetUtcNow(), "analyst-1"), Ct);

        Assert.Equal(0.8, (await store.GetActiveAsync("stepup", "m1", Ct))!.Value);
        Assert.Equal("analyst-1", (await store.GetActiveAsync("stepup", "m1", Ct))!.UpdatedBy);
        Assert.Equal(0.5, (await store.GetActiveAsync("stepup", "m2", Ct))!.Value);
        Assert.Null(await store.GetActiveAsync("stepup", "m3", Ct));
        Assert.Equal(3, (await store.ListAsync(Ct)).Count);
    }

    [Fact]
    public async Task Enrollment_RoundTripsProtectedAndExpires()
    {
        var store = _db.Get<IEnrollmentStore>();
        var id = Guid.NewGuid();
        await store.SaveAsync(new EnrollmentState(id, Alice, ["a b c d"], [[1f, 2f]], _db.Time.GetUtcNow().AddMinutes(10)), Ct);

        var loaded = await store.GetAsync(id, _db.Time.GetUtcNow(), Ct);
        _db.Time.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(["a b c d"], loaded!.Phrases);
        Assert.Equal([1f, 2f], loaded.Samples.Single());
        Assert.Null(await store.GetAsync(id, _db.Time.GetUtcNow(), Ct));
    }

    [Fact]
    public async Task UsedAssertions_AcceptEachJtiOnce()
    {
        var store = _db.Get<IUsedAssertionStore>();

        Assert.True(await store.TryUseAsync("j1", _db.Time.GetUtcNow(), Ct));
        Assert.False(await store.TryUseAsync("j1", _db.Time.GetUtcNow(), Ct));
        Assert.True(await store.TryUseAsync("j2", _db.Time.GetUtcNow(), Ct));
    }

    [Fact]
    public void UserKey_IsAKeyedHashNotTheRawIds()
    {
        var hasher = _db.Get<IUserKeyHasher>();

        var key = hasher.Hash("tid-1", "oid-1");

        Assert.Equal(key, hasher.Hash("tid-1", "oid-1"));
        Assert.NotEqual(key, hasher.Hash("tid-1", "oid-2"));
        Assert.DoesNotContain("oid-1", key.Value, StringComparison.Ordinal);
        Assert.NotEqual(key, new HmacUserKeyHasher(new byte[32]).Hash("tid-1", "oid-1"));
    }

    [Fact]
    public void KeyStore_PersistsTheSigningKeyAndKeepsItOutOfPlaintext()
    {
        var first = _db.Get<FileKeyStore>();
        var second = new FileKeyStore(_db.KeyDirectory, _db.Get<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>());
        var fileBytes = File.ReadAllBytes(Path.Combine(_db.KeyDirectory, "assertion-signing.key"));

        Assert.Equal(first.SigningKey.KeyId, second.SigningKey.KeyId);
        Assert.Equal(first.UserKeySecret, second.UserKeySecret);
        Assert.False(fileBytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes("PRIVATE KEY")) >= 0);
    }
}
