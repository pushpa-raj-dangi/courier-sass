using Courier.Persistence;
using Courier.Platform.Domain;
using Courier.Platform.Infrastructure.Configuration;
using Courier.Platform.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

            var cache = new PlatformSettingsCache(scopeFactory);
            await cache.RefreshAsync();

            Assert.Equal("s3cr3t", cache.TryGetSecret($"Test:{key}"));
        }
    }

    [Fact]
    public void TryGetSecret_NothingCachedYet_ReturnsNull()
    {
        var cache = new PlatformSettingsCache(BuildScopeFactory());

        Assert.Null(cache.TryGetSecret("Esewa:SecretKey"));
    }
}
