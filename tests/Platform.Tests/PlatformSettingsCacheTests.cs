using Courier.Persistence;
using Courier.Platform.Domain;
using Courier.Platform.Infrastructure.Configuration;
using Courier.Platform.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformSettingsCacheTests
{
    private sealed class NullTenantProvider : Courier.SharedKernel.ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }

    private static IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Courier.SharedKernel.ICurrentTenantProvider>(new NullTenantProvider());
        services.AddScoped(sp => TestDbContextFactory.Create(sp.GetRequiredService<Courier.SharedKernel.ICurrentTenantProvider>()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddDataProtection();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task RefreshAsync_SecretOverrideExists_DecryptsAndCachesIt()
    {
        var scopeFactory = BuildScopeFactory();
        using (var scope = scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("PlatformSettings.Secrets");
            var key = $"Esewa.SecretKey.{Guid.NewGuid():N}";
            dbContext.Set<PlatformSettingDefinition>().Add(new PlatformSettingDefinition { Key = key, Category = "PaymentGateway", ConfigPath = $"Test:{key}", ValueType = "String", IsSecret = true });
            dbContext.Set<PlatformSetting>().Add(new PlatformSetting { Key = key, Value = protector.Protect("s3cr3t"), IsOverridden = true, UpdatedAt = DateTime.UtcNow, UpdatedByUserId = Guid.NewGuid() });
            await dbContext.SaveChangesAsync();

            try
            {
                var cache = new PlatformSettingsCache(scopeFactory, NullLogger<PlatformSettingsCache>.Instance);
                await cache.RefreshAsync();

                Assert.Equal("s3cr3t", cache.TryGetSecret($"Test:{key}"));
            }
            finally
            {
                // This test's DataProtection provider is built fresh in-process (BuildScopeFactory's
                // AddDataProtection() call has no PersistKeysToDbContext), so its key ring is
                // throwaway and never persisted. Ciphertext protected with it can never be
                // decrypted again — by this test run or the real app — so the row must not be
                // left behind in the shared dev Postgres, or PlatformSettingsCache.RefreshAsync
                // would hit it as permanently-undecryptable garbage on every future run.
                await CleanupSettingAsync(dbContext, key);
            }
        }
    }

    private static async Task CleanupSettingAsync(DbContext dbContext, string key)
    {
        var setting = await dbContext.Set<PlatformSetting>().FirstOrDefaultAsync(s => s.Key == key);
        if (setting is not null)
        {
            dbContext.Remove(setting);
        }

        var definition = await dbContext.Set<PlatformSettingDefinition>().FirstOrDefaultAsync(d => d.Key == key);
        if (definition is not null)
        {
            dbContext.Remove(definition);
        }

        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public void TryGetSecret_NothingCachedYet_ReturnsNull()
    {
        var cache = new PlatformSettingsCache(BuildScopeFactory(), NullLogger<PlatformSettingsCache>.Instance);

        Assert.Null(cache.TryGetSecret("Esewa:SecretKey"));
    }
}
