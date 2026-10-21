using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StepUp.Core;
using StepUp.Infrastructure.Persistence;
using StepUp.Infrastructure.Security;

namespace StepUp.Infrastructure.Tests;

public sealed class HardeningTests : IDisposable
{
    private static readonly UserKey Alice = new("alice");

    private readonly TestDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SecureDeleteInterceptor_SetsThePragmaOnANewConnection()
    {
        await using var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct);
        await context.Database.OpenConnectionAsync(Ct);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA secure_delete;";

        Assert.Equal(1L, await command.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task Challenges_ExpiredPastTheMargin_ArePurgedOnAdd()
    {
        var store = _db.Get<IChallengeStore>();
        var now = _db.Time.GetUtcNow();
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();
        await store.AddAsync(new Challenge(old, Alice, "a", "p", now.AddMinutes(1), null), Ct);
        _db.Time.Advance(TimeSpan.FromMinutes(2));
        await store.AddAsync(new Challenge(recent, Alice, "a", "p", _db.Time.GetUtcNow().AddMinutes(1), null), Ct);
        Assert.NotNull(await store.GetAsync(old, Ct));

        _db.Time.Advance(TimeSpan.FromHours(2));
        await store.AddAsync(new Challenge(Guid.NewGuid(), Alice, "a", "p", _db.Time.GetUtcNow().AddMinutes(1), null), Ct);

        Assert.Null(await store.GetAsync(old, Ct));
        Assert.Null(await store.GetAsync(recent, Ct));
    }

    [Fact]
    public async Task UsedAssertions_PastTheMargin_ArePurged_AndRecentOnesStillBlockReplay()
    {
        var store = _db.Get<IUsedAssertionStore>();
        var now = _db.Time.GetUtcNow();
        Assert.True(await store.TryUseAsync("old", now.AddMinutes(5), Ct));

        _db.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(await store.TryUseAsync("recent", _db.Time.GetUtcNow().AddMinutes(5), Ct));
        Assert.False(await store.TryUseAsync("old", now.AddMinutes(5), Ct));

        _db.Time.Advance(TimeSpan.FromHours(2));
        Assert.True(await store.TryUseAsync("later", _db.Time.GetUtcNow().AddMinutes(5), Ct));

        await using var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(["later"], await context.UsedAssertions.Select(u => u.Jti).ToListAsync(Ct));
    }

    [Fact]
    public async Task Eraser_DeletesVoiceprintAndEnrollments_WithdrawsConsent_AndPseudonymisesAttempts()
    {
        var now = _db.Time.GetUtcNow();
        await _db.Get<IVoiceprintStore>().ReplaceAsync(new Voiceprint(Alice, "m", [0.5f], now), Ct);
        await _db.Get<IEnrollmentStore>().SaveAsync(new EnrollmentState(Guid.NewGuid(), Alice, ["a"], [[1f]], now.AddMinutes(5)), Ct);
        await _db.Get<IConsentStore>().GiveAsync(Alice, "v1", Ct);
        await _db.Get<IAttemptLog>().AppendAsync(new Attempt(Guid.NewGuid(), Alice, now, "a", 0.9, 0.8, "m", AttemptResult.Pass, null, AttemptMode.Voice), Ct);

        await _db.Get<IUserDataEraser>().EraseAsync(Alice, Ct);

        await using var context = await _db.Get<IDbContextFactory<StepUpDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(0, await context.Voiceprints.CountAsync(Ct));
        Assert.Equal(0, await context.Enrollments.CountAsync(Ct));
        Assert.False(await _db.Get<IConsentStore>().IsActiveAsync(Alice, Ct));
        var attempt = await context.Attempts.SingleAsync(Ct);
        Assert.StartsWith("anon-", attempt.UserKey, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyFilesAndDirectories_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var dir = Path.Combine(_db.KeyDirectory, "private", "keys");
        _ = new FileKeyStore(dir, new EphemeralDataProtectionProvider());

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
        foreach (var file in Directory.GetFiles(dir))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }

        Assert.Equal(2, Directory.GetFiles(dir).Length);
    }
}
