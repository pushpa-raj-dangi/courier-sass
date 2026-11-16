using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Identity.Tests;

public class UserDirectoryLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static Organization BuildOrganization(Guid id) => new() { Id = id, Name = $"Org {id}", Slug = $"org-{id:N}" };

    private static User BuildUser(Guid organizationId, string fullName) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, Email = $"user-{Guid.NewGuid():N}@example.com",
        PasswordHash = "not-a-real-hash", FullName = fullName
    };

    [Fact]
    public async Task GetFullNamesAsync_ReturnsNamesOfThisTenantsUsersOnly()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        dbContext.Add(BuildOrganization(orgA));
        dbContext.Add(BuildOrganization(orgB));
        var asha = BuildUser(orgA, "Asha Gurung");
        var bikash = BuildUser(orgA, "Bikash Rai");
        var otherTenant = BuildUser(orgB, "Other Tenant");
        dbContext.AddRange(asha, bikash, otherTenant);
        await dbContext.SaveChangesAsync();
        tenantProvider.OrganizationId = orgA;
        var service = new UserDirectoryLookupService(dbContext);

        var names = await service.GetFullNamesAsync(new[] { asha.Id, bikash.Id, otherTenant.Id, Guid.NewGuid(), asha.Id });

        Assert.Equal(2, names.Count);
        Assert.Equal("Asha Gurung", names[asha.Id]);
        Assert.Equal("Bikash Rai", names[bikash.Id]);
        Assert.False(names.ContainsKey(otherTenant.Id));
    }

    [Fact]
    public async Task GetFullNamesAsync_EmptyInput_RunsNoQuery()
    {
        // A disposed context throws on any query, so an empty result proves no query ran.
        var dbContext = TestDbContextFactory.Create(new FixedTenantProvider { OrganizationId = Guid.NewGuid() });
        await dbContext.DisposeAsync();
        var service = new UserDirectoryLookupService(dbContext);

        var names = await service.GetFullNamesAsync(Array.Empty<Guid>());

        Assert.Empty(names);
    }
}
