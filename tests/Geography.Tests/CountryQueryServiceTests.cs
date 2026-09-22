using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class CountryQueryServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
            modelBuilder.ApplyConfiguration(new CountryConfiguration());
        }
    }

    private static TestDbContext CreateSeededContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task ListServicedAsync_ExcludesUnservicedCountries()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryQueryService(dbContext);

        var serviced = await service.ListServicedAsync();
        Assert.Empty(serviced);

        var nepal = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "NP");
        nepal.IsServiced = true;
        await dbContext.SaveChangesAsync();

        serviced = await service.ListServicedAsync();
        Assert.Equal("NP", Assert.Single(serviced).IsoCode);
    }

    [Fact]
    public async Task ListForAdminAsync_IncludesUnservicedRows_AndPaginates()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryQueryService(dbContext);
        var total = await dbContext.Set<Country>().CountAsync();

        var page1 = await service.ListForAdminAsync(search: null, page: 1, pageSize: 10);

        Assert.Equal(total, page1.TotalCount);
        Assert.Equal(10, page1.Items.Count);
    }

    [Fact]
    public async Task ListForAdminAsync_SearchMatchesIsoCodeOrName_CaseInsensitive()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryQueryService(dbContext);

        var byCode = await service.ListForAdminAsync(search: "np", page: 1, pageSize: 10);
        Assert.Equal("NP", Assert.Single(byCode.Items).IsoCode);

        var byName = await service.ListForAdminAsync(search: "nepal", page: 1, pageSize: 10);
        Assert.Equal("NP", Assert.Single(byName.Items).IsoCode);
    }

    [Fact]
    public async Task ListForAdminAsync_IncludesZoneName_WhenAssigned()
    {
        await using var dbContext = CreateSeededContext();
        var zone = new CountryZone { Id = Guid.NewGuid(), Name = "SAARC", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<CountryZone>().Add(zone);
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");
        india.CountryZoneId = zone.Id;
        await dbContext.SaveChangesAsync();
        var service = new CountryQueryService(dbContext);

        var result = await service.ListForAdminAsync(search: "IN", page: 1, pageSize: 10);

        Assert.Equal("SAARC", Assert.Single(result.Items).CountryZoneName);
    }
}
