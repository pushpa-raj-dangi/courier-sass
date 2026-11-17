using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;

namespace Courier.Identity.Tests;

public class OrganizationTimeZoneLookupTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private static async Task<Guid> SeedAsync(Courier.Persistence.AppDbContext dbContext, string? timeZoneId)
    {
        var org = new Organization { Id = Guid.NewGuid(), Name = "Tz Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Add(org);
        if (timeZoneId is not null)
        {
            dbContext.Add(new OrganizationSettings { OrganizationId = org.Id, TimeZoneId = timeZoneId });
        }
        await dbContext.SaveChangesAsync();
        return org.Id;
    }

    [Fact]
    public async Task GetTimeZoneAsync_KathmanduId_ReturnsNepalOffset()
    {
        await using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var orgId = await SeedAsync(dbContext, "Asia/Kathmandu");
        var tz = await new OrganizationTimeZoneLookup(dbContext).GetTimeZoneAsync(orgId);
        Assert.Equal(TimeSpan.FromMinutes(345), tz.BaseUtcOffset);
    }

    [Fact]
    public async Task GetTimeZoneAsync_UnknownId_FallsBackToNepal()
    {
        await using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var orgId = await SeedAsync(dbContext, "Mars/Base");
        var tz = await new OrganizationTimeZoneLookup(dbContext).GetTimeZoneAsync(orgId);
        Assert.Equal(TimeSpan.FromMinutes(345), tz.BaseUtcOffset);
    }

    [Fact]
    public async Task GetTimeZoneAsync_MissingRow_FallsBackToNepal()
    {
        await using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var tz = await new OrganizationTimeZoneLookup(dbContext).GetTimeZoneAsync(Guid.NewGuid());
        Assert.Equal(TimeSpan.FromMinutes(345), tz.BaseUtcOffset);
    }
}
