using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StepUp.Core;
using StepUp.Infrastructure.Persistence;
using StepUp.Infrastructure.Security;

namespace StepUp.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers persistence, key handling and the Core services. The caller adds Data Protection and an <see cref="IVerifier"/>.</summary>
    public static IServiceCollection AddStepUpInfrastructure(this IServiceCollection services, string databasePath, string keyDirectory)
    {
        services.AddDbContextFactory<StepUpDbContext>(o => o
            .UseSqlite($"Data Source={databasePath}")
            .AddInterceptors(new SecureDeleteInterceptor()));
        services.AddSingleton(sp => new FileKeyStore(keyDirectory, sp.GetRequiredService<IDataProtectionProvider>()));
        services.AddSingleton<IAssertionKeyProvider>(sp => sp.GetRequiredService<FileKeyStore>());
        services.AddSingleton<IUserKeyHasher>(sp => new HmacUserKeyHasher(sp.GetRequiredService<FileKeyStore>().UserKeySecret));
        services.AddSingleton<EmbeddingProtector>();
        services.AddSingleton<IChallengeStore, ChallengeStore>();
        services.AddSingleton<IVoiceprintStore, VoiceprintStore>();
        services.AddSingleton<IConsentStore, ConsentStore>();
        services.AddSingleton<IAttemptLog, AttemptLog>();
        services.AddSingleton<IThresholdStore, ThresholdStore>();
        services.AddSingleton<IEnrollmentStore, EnrollmentStore>();
        services.AddSingleton<IUsedAssertionStore, UsedAssertionStore>();
        return services;
    }
}
