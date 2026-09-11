using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

public class OrganizationAdminServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task ReactivateIfSystemSuspendedAsync_SystemSuspended_Reactivates()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = false, SuspendedBySystem = true };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

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
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

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
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        Assert.True(await service.IsActiveAsync(organization.Id));
    }

    [Fact]
    public async Task IsActiveAsync_SuspendedOrganization_ReturnsFalse()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = false };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        Assert.False(await service.IsActiveAsync(organization.Id));
    }

    [Fact]
    public async Task IsActiveAsync_SoftDeletedOrganization_ReturnsFalse()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true, DeletedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        Assert.False(await service.IsActiveAsync(organization.Id));
    }

    [Fact]
    public async Task ListAsync_NoFilters_ReturnsAllOrderedByName()
    {
        // TestDbContextFactory runs against the real shared dev Postgres (tens of thousands of
        // pre-existing organizations from other tests/dev usage), not an isolated database — a
        // unique random prefix scopes this test's "no filters" assertion to only its own rows via
        // the search param, rather than asserting over the whole shared table.
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var prefix = $"NoFilters-{Guid.NewGuid():N}";
        var orgB = new Organization { Id = Guid.NewGuid(), Name = $"{prefix} Bravo Courier", Slug = $"bravo-{Guid.NewGuid():N}", IsActive = true };
        var orgA = new Organization { Id = Guid.NewGuid(), Name = $"{prefix} Alpha Courier", Slug = $"alpha-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().AddRange(orgB, orgA);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var result = await service.ListAsync(search: prefix, isActive: null, isDeleted: null, page: null, pageSize: null);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { $"{prefix} Alpha Courier", $"{prefix} Bravo Courier" }, result.Items.Select(o => o.Name));
    }

    [Fact]
    public async Task ListAsync_SearchMatchesNameSubstring_CaseInsensitive()
    {
        // Runs against the shared, persistent dev DB — a literal fixed name would accumulate
        // duplicate rows across repeated test runs and break Assert.Single(); a random suffix in
        // both the matching name and the search term keeps this run's assertion isolated.
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var match = new Organization { Id = Guid.NewGuid(), Name = $"Kathmandu Couriers {suffix}", Slug = $"ktm-{Guid.NewGuid():N}", IsActive = true };
        var nonMatch = new Organization { Id = Guid.NewGuid(), Name = $"Pokhara Logistics {suffix}", Slug = $"pkr-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().AddRange(match, nonMatch);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var result = await service.ListAsync(search: $"kathmandu couriers {suffix}", isActive: null, isDeleted: null, page: null, pageSize: null);

        var single = Assert.Single(result.Items);
        Assert.Equal(match.Id, single.Id);
    }

    [Fact]
    public async Task ListAsync_SearchMatchesSlugSubstring()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var match = new Organization { Id = Guid.NewGuid(), Name = "Some Org", Slug = $"unique-slug-{suffix}", IsActive = true };
        var nonMatch = new Organization { Id = Guid.NewGuid(), Name = "Other Org", Slug = $"different-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().AddRange(match, nonMatch);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var result = await service.ListAsync(search: suffix, isActive: null, isDeleted: null, page: null, pageSize: null);

        var single = Assert.Single(result.Items);
        Assert.Equal(match.Id, single.Id);
    }

    [Fact]
    public async Task ListAsync_SearchMatchesUserEmailWithinOrganization()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var match = new Organization { Id = Guid.NewGuid(), Name = "Org With Owner", Slug = $"owner-{Guid.NewGuid():N}", IsActive = true };
        var nonMatch = new Organization { Id = Guid.NewGuid(), Name = "Org No Match", Slug = $"nomatch-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().AddRange(match, nonMatch);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"owner-{suffix}@example.test";
        dbContext.Set<User>().Add(new User { Id = Guid.NewGuid(), OrganizationId = match.Id, Email = email, PasswordHash = "hash", FullName = "Owner Person", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var result = await service.ListAsync(search: suffix, isActive: null, isDeleted: null, page: null, pageSize: null);

        var single = Assert.Single(result.Items);
        Assert.Equal(match.Id, single.Id);
    }

    [Fact]
    public async Task ListAsync_IsActiveFilter_ReturnsOnlyMatchingOrganizations()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var prefix = $"ActiveFilter-{Guid.NewGuid():N}";
        var active = new Organization { Id = Guid.NewGuid(), Name = $"{prefix} Active Org", Slug = $"active-{Guid.NewGuid():N}", IsActive = true };
        var suspended = new Organization { Id = Guid.NewGuid(), Name = $"{prefix} Suspended Org", Slug = $"suspended-{Guid.NewGuid():N}", IsActive = false };
        dbContext.Set<Organization>().AddRange(active, suspended);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var result = await service.ListAsync(search: prefix, isActive: true, isDeleted: null, page: null, pageSize: null);

        var single = Assert.Single(result.Items);
        Assert.Equal(active.Id, single.Id);
    }

    [Fact]
    public async Task ListAsync_IsDeletedFilter_ReturnsOnlyMatchingOrganizations()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var prefix = $"DeletedFilter-{Guid.NewGuid():N}";
        var deleted = new Organization { Id = Guid.NewGuid(), Name = $"{prefix} Deleted Org", Slug = $"deleted-{Guid.NewGuid():N}", IsActive = false, DeletedAt = DateTimeOffset.UtcNow };
        var notDeleted = new Organization { Id = Guid.NewGuid(), Name = $"{prefix} Live Org", Slug = $"live-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().AddRange(deleted, notDeleted);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var result = await service.ListAsync(search: prefix, isActive: null, isDeleted: true, page: null, pageSize: null);

        var single = Assert.Single(result.Items);
        Assert.Equal(deleted.Id, single.Id);
    }

    [Fact]
    public async Task ListAsync_Pagination_ReturnsCorrectSliceAndTotalCount()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var prefix = $"Pagination-{Guid.NewGuid():N}";
        for (var i = 0; i < 5; i++)
        {
            dbContext.Set<Organization>().Add(new Organization { Id = Guid.NewGuid(), Name = $"{prefix} Org {i:D2}", Slug = $"org-{i}-{Guid.NewGuid():N}", IsActive = true });
        }
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var page1 = await service.ListAsync(search: prefix, isActive: null, isDeleted: null, page: 1, pageSize: 2);
        var page2 = await service.ListAsync(search: prefix, isActive: null, isDeleted: null, page: 2, pageSize: 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(new[] { $"{prefix} Org 00", $"{prefix} Org 01" }, page1.Items.Select(o => o.Name));
        Assert.Equal(new[] { $"{prefix} Org 02", $"{prefix} Org 03" }, page2.Items.Select(o => o.Name));
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
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var result = await service.GetAsync(organization.Id);

        Assert.NotNull(result);
        Assert.Equal(organization.Id, result!.Id);
        Assert.Equal(organization.Name, result.Name);
        Assert.Equal(organization.Slug, result.Slug);
    }

    [Fact]
    public async Task ListUsersAsync_ScopesToOrganizationAndPaginates()
    {
        // Regression guard for IgnoreQueryFilters(): called only from Platform's
        // PlatformDashboardService (null ambient tenant), same reasoning as GetAsync above.
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        var otherOrganization = new Organization { Id = Guid.NewGuid(), Name = "Other Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().AddRange(organization, otherOrganization);
        for (var i = 0; i < 3; i++)
        {
            dbContext.Set<User>().Add(new User { Id = Guid.NewGuid(), OrganizationId = organization.Id, Email = $"user{i}-{Guid.NewGuid():N}@example.test", PasswordHash = "hash", FullName = $"User {i}", IsActive = true });
        }
        dbContext.Set<User>().Add(new User { Id = Guid.NewGuid(), OrganizationId = otherOrganization.Id, Email = $"other-{Guid.NewGuid():N}@example.test", PasswordHash = "hash", FullName = "Other Org User", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext, new FakeAuditLogger());

        var page1 = await service.ListUsersAsync(organization.Id, page: 1, pageSize: 2);
        var page2 = await service.ListUsersAsync(organization.Id, page: 2, pageSize: 2);

        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(3, page1.TotalCount);
        Assert.DoesNotContain(page1.Items.Concat(page2.Items), u => u.FullName == "Other Org User");
    }
}
