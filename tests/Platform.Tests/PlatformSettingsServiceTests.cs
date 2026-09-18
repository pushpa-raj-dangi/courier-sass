using Courier.Audit.Application;
using Courier.Platform.Application;
using Courier.Platform.Infrastructure;
using Courier.Platform.Infrastructure.Configuration;
using Courier.Platform.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformSettingsServiceTests
{
    private sealed class NullTenantProvider : Courier.SharedKernel.ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopCache : IPlatformSettingsCache
    {
        public string? TryGetSecret(string configPath) => null;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (PlatformSettingsService Service, FakeAuditLogger AuditLogger) BuildService()
    {
        var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var services = new ServiceCollection();
        services.AddDataProtection();
        var dataProtectionProvider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        var configuration = new ConfigurationBuilder().Build();
        // Same hardcoded dev-container address every other Redis-touching test in this repo
        // uses (RedisRateLimiterTests.cs, RedisIdempotencyStoreTests.cs) — matches
        // docker-compose's Redis port mapping, not guessed from the Postgres connection string.
        var redis = ConnectionMultiplexer.Connect("localhost:6380");
        var auditLogger = new FakeAuditLogger();
        var service = new PlatformSettingsService(dbContext, auditLogger, dataProtectionProvider, redis, configuration, new NoopCache());
        return (service, auditLogger);
    }

    // These tests exercise EXISTING seeded catalog keys (Frontend.BaseUrl, Esewa.SecretKey,
    // Billing.GraceDays, Jwt.SigningKey) against the shared dev Postgres used by every other
    // worktree/task. Any test that successfully persists an override must reset that key back to
    // IsOverridden=false/Value=null in a finally block, or it permanently pollutes the shared
    // catalog state for every other test/task reading it afterward (see e3963bc — this exact
    // mistake already happened once with Task 4's cache tests).

    [Fact]
    public async Task UpdateAsync_NonSecretKey_PersistsPlaintextAndAuditsRealValues()
    {
        var (service, auditLogger) = BuildService();
        const string key = "Frontend.BaseUrl";

        try
        {
            await service.UpdateAsync(key, "https://example.test", null, Guid.NewGuid());
            var all = await service.GetAllAsync();

            var setting = all.Single(s => s.Key == key);
            Assert.True(setting.IsOverridden);
            Assert.Equal("https://example.test", setting.DisplayValue);
            var entry = auditLogger.Entries.Single(e => e.Action == "platform_setting.updated" && e.EntityId == key);
            Assert.Equal("https://example.test", entry.After);
            Assert.True(entry.IsCrossTenantAction);
            Assert.Null(entry.OrganizationId);
        }
        finally
        {
            await service.ResetAsync(key, Guid.NewGuid());
        }
    }

    [Fact]
    public async Task UpdateAsync_SecretKey_MasksDisplayValueAndRedactsAudit()
    {
        var (service, auditLogger) = BuildService();
        const string key = "Esewa.SecretKey";

        try
        {
            await service.UpdateAsync(key, "super-secret-value", null, Guid.NewGuid());
            var all = await service.GetAllAsync();

            var setting = all.Single(s => s.Key == key);
            Assert.True(setting.IsOverridden);
            Assert.DoesNotContain("super-secret-value", setting.DisplayValue);
            var entry = auditLogger.Entries.Single(e => e.Action == "platform_setting.updated" && e.EntityId == key);
            Assert.Equal("[REDACTED]", entry.After);
        }
        finally
        {
            await service.ResetAsync(key, Guid.NewGuid());
        }
    }

    [Fact]
    public async Task UpdateAsync_IntValueTypeWithNonNumericValue_ThrowsValidationException()
    {
        var (service, _) = BuildService();

        // Validation fails before any row is written — no override to clean up.
        await Assert.ThrowsAsync<PlatformSettingsValidationException>(
            () => service.UpdateAsync("Billing.GraceDays", "not-a-number", null, Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_RequiresConfirmationKeyWithoutPhrase_ThrowsValidationException()
    {
        var (service, _) = BuildService();

        // Confirmation check fails before any row is written — no override to clean up.
        await Assert.ThrowsAsync<PlatformSettingsValidationException>(
            () => service.UpdateAsync("Jwt.SigningKey", "a-new-signing-key-value", null, Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_RequiresConfirmationKeyWithCorrectPhrase_Succeeds()
    {
        var (service, _) = BuildService();
        const string key = "Jwt.SigningKey";

        try
        {
            await service.UpdateAsync(key, "a-new-signing-key-value", "REVOKE ALL SESSIONS", Guid.NewGuid());
            var all = await service.GetAllAsync();

            Assert.True(all.Single(s => s.Key == key).IsOverridden);
        }
        finally
        {
            await service.ResetAsync(key, Guid.NewGuid());
        }
    }

    [Fact]
    public async Task ResetAsync_PreviouslyOverriddenKey_ClearsOverride()
    {
        var (service, auditLogger) = BuildService();
        const string key = "Frontend.BaseUrl";

        try
        {
            await service.UpdateAsync(key, "https://example.test", null, Guid.NewGuid());

            await service.ResetAsync(key, Guid.NewGuid());
            var all = await service.GetAllAsync();

            var setting = all.Single(s => s.Key == key);
            Assert.False(setting.IsOverridden);
            Assert.Null(setting.DisplayValue);

            var entry = auditLogger.Entries.Single(e => e.Action == "platform_setting.reset" && e.EntityId == key);
            Assert.Equal("https://example.test", entry.Before);
            Assert.Null(entry.After);
        }
        finally
        {
            // Belt-and-braces: guarantees the key is unoverridden even if an assertion above
            // throws before the explicit ResetAsync call runs.
            await service.ResetAsync(key, Guid.NewGuid());
        }
    }

    [Fact]
    public async Task ResetAsync_SecretKey_RedactsBeforeValueInAudit()
    {
        var (service, auditLogger) = BuildService();
        const string key = "Esewa.SecretKey";

        try
        {
            await service.UpdateAsync(key, "super-secret-value", null, Guid.NewGuid());

            await service.ResetAsync(key, Guid.NewGuid());
            var all = await service.GetAllAsync();

            var setting = all.Single(s => s.Key == key);
            Assert.False(setting.IsOverridden);

            var entry = auditLogger.Entries.Single(e => e.Action == "platform_setting.reset" && e.EntityId == key);
            Assert.Equal("[REDACTED]", entry.Before);
            Assert.Null(entry.After);
        }
        finally
        {
            await service.ResetAsync(key, Guid.NewGuid());
        }
    }

    [Fact]
    public async Task GetAllAsync_UnknownDefaultState_ReturnsAllCatalogEntriesNotOverridden()
    {
        var (service, _) = BuildService();

        var all = await service.GetAllAsync();

        // Verify the 19 expected catalog keys are present and not overridden.
        // Tolerates extra orphaned rows from prior test debris in the shared Postgres.
        foreach (var expectedKey in PlatformSettingCatalog.Definitions.Select(d => d.Key))
        {
            var setting = Assert.Single(all, s => s.Key == expectedKey);
            Assert.False(setting.IsOverridden, $"Setting {expectedKey} should not be overridden");
        }
    }
}
