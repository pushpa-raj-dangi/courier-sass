using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

public class OrganizationAdminServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task ReactivateIfSystemSuspendedAsync_SystemSuspended_Reactivates()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = false, SuspendedBySystem = true };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        await service.ReactivateIfSystemSuspendedAsync(organization.Id);

        var reloaded = dbContext.Set<Organization>().IgnoreQueryFilters().Single(o => o.Id == organization.Id);
        Assert.True(reloaded.IsActive);
        Assert.False(reloaded.SuspendedBySystem);
    }

    [Fact]
    public async Task ReactivateIfSystemSuspendedAsync_ManuallySuspended_DoesNotReactivate()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = false, SuspendedBySystem = false };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        await service.ReactivateIfSystemSuspendedAsync(organization.Id);

        var reloaded = dbContext.Set<Organization>().IgnoreQueryFilters().Single(o => o.Id == organization.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task IsActiveAsync_ActiveOrganization_ReturnsTrue()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        Assert.True(await service.IsActiveAsync(organization.Id));
    }

    [Fact]
    public async Task IsActiveAsync_SuspendedOrganization_ReturnsFalse()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = false };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        Assert.False(await service.IsActiveAsync(organization.Id));
    }

    [Fact]
    public async Task IsActiveAsync_SoftDeletedOrganization_ReturnsFalse()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true, DeletedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        Assert.False(await service.IsActiveAsync(organization.Id));
    }

    [Fact]
    public async Task GetAsync_WithNullAmbientTenant_ReturnsOrganization()
    {
        // Regression guard: GetAsync is called exclusively from Platform's
        // PlatformDashboardService.GetOrganizationDetailAsync, which always runs on a
        // SuperAdmin/Platform JWT whose ambient tenant is null. AppDbContext's Organization
        // query filter fails closed (matches nothing) whenever ICurrentTenantProvider.OrganizationId
        // is null, so without IgnoreQueryFilters() this would always return null.
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        var result = await service.GetAsync(organization.Id);

        Assert.NotNull(result);
        Assert.Equal(organization.Id, result!.Id);
        Assert.Equal(organization.Name, result.Name);
        Assert.Equal(organization.Slug, result.Slug);
    }
}
