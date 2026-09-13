using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

public class OrganizationSettingsServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private static Organization NewOrganization(DbContext dbContext)
    {
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().Add(organization);
        return organization;
    }

    [Fact]
    public async Task GetAsync_NoRowExists_CreatesRowWithDefaults()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger());

        var result = await service.GetAsync(organization.Id);

        Assert.Equal("Asia/Kathmandu", result.Profile.TimeZoneId);
        Assert.Equal(7, result.Security.RefreshTokenLifetimeDays);
        Assert.Equal(8, result.Security.MinPasswordLength);
        Assert.Null(result.Defaults.DefaultServiceTypeId);
        var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
        Assert.Equal(7, persisted.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task GetAsync_RowAlreadyExists_ReturnsExistingValuesWithoutOverwriting()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        dbContext.Set<OrganizationSettings>().Add(new OrganizationSettings { OrganizationId = organization.Id, RefreshTokenLifetimeDays = 14, MinPasswordLength = 10, TimeZoneId = "Asia/Kathmandu" });
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger());

        var result = await service.GetAsync(organization.Id);

        Assert.Equal(14, result.Security.RefreshTokenLifetimeDays);
        Assert.Equal(10, result.Security.MinPasswordLength);
        Assert.Single(dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Where(s => s.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task GetSecurityPolicyAsync_NoRowExists_ReturnsDefaultsAndPersistsRow()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger());

        var result = await service.GetSecurityPolicyAsync(organization.Id);

        Assert.Equal(7, result.RefreshTokenLifetimeDays);
        Assert.Equal(8, result.MinPasswordLength);
    }
}
