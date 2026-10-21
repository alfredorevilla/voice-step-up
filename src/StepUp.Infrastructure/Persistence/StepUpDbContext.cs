using Microsoft.EntityFrameworkCore;

namespace StepUp.Infrastructure.Persistence;

public sealed class StepUpDbContext(DbContextOptions<StepUpDbContext> options) : DbContext(options)
{
    public DbSet<ConsentRow> Consents => Set<ConsentRow>();
    public DbSet<VoiceprintRow> Voiceprints => Set<VoiceprintRow>();
    public DbSet<EnrollmentRow> Enrollments => Set<EnrollmentRow>();
    public DbSet<AttemptRow> Attempts => Set<AttemptRow>();
    public DbSet<ChallengeRow> Challenges => Set<ChallengeRow>();
    public DbSet<UsedAssertionRow> UsedAssertions => Set<UsedAssertionRow>();
    public DbSet<ThresholdRow> Thresholds => Set<ThresholdRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsentRow>().HasIndex(x => x.UserKey);
        modelBuilder.Entity<VoiceprintRow>().HasIndex(x => x.UserKey).IsUnique();
        modelBuilder.Entity<EnrollmentRow>().HasIndex(x => x.UserKey);
        modelBuilder.Entity<AttemptRow>().HasIndex(x => new { x.UserKey, x.At });
        modelBuilder.Entity<ChallengeRow>().HasIndex(x => x.UserKey);
        modelBuilder.Entity<UsedAssertionRow>().HasKey(x => x.Jti);
        modelBuilder.Entity<ThresholdRow>().HasIndex(x => new { x.Name, x.ModelId, x.UpdatedAt });
    }

    /// <summary>Attempts are append only: any update or delete of an attempt row fails.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAttempts();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAttempts();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAttempts()
    {
        if (ChangeTracker.Entries<AttemptRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Attempts are append only.");
        }
    }
}
