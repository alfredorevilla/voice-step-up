using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StepUp.Infrastructure;
using StepUp.Infrastructure.Persistence;

namespace StepUp.Infrastructure.Tests;

/// <summary>A real file-backed SQLite database in a temp directory, with ephemeral Data Protection keys.</summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stepup-tests-" + Guid.NewGuid().ToString("N"));

    public TestDatabase()
    {
        Directory.CreateDirectory(_root);
        DbPath = Path.Combine(_root, "stepup.db");
        KeyDirectory = Path.Combine(_root, "keys");
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddStepUpInfrastructure(DbPath, KeyDirectory);
        Provider = services.BuildServiceProvider();
        using var db = Provider.GetRequiredService<IDbContextFactory<StepUpDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public Microsoft.Extensions.Time.Testing.FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    public string DbPath { get; }

    public string KeyDirectory { get; }

    public ServiceProvider Provider { get; }

    public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    public void Dispose()
    {
        Provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
