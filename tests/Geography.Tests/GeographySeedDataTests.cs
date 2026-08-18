using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class GeographySeedDataTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ProvinceConfiguration());
            modelBuilder.ApplyConfiguration(new DistrictConfiguration());
            modelBuilder.ApplyConfiguration(new LocalLevelConfiguration());
            modelBuilder.ApplyConfiguration(new WardConfiguration());
            modelBuilder.ApplyConfiguration(new AreaConfiguration());
        }
    }

    private static TestDbContext CreateSeededContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task Seed_ProducesExpectedRowCounts()
    {
        await using var dbContext = CreateSeededContext();

        Assert.Equal(7, await dbContext.Set<Province>().CountAsync());
        Assert.Equal(77, await dbContext.Set<District>().CountAsync());
        Assert.Equal(5, await dbContext.Set<LocalLevel>().CountAsync());
        Assert.Equal(15, await dbContext.Set<Ward>().CountAsync()); // 5 local levels x 3 wards
        Assert.Equal(30, await dbContext.Set<Area>().CountAsync()); // 15 wards x 2 areas
    }

    [Fact]
    public async Task Seed_KathmanduChain_NestsCorrectly()
    {
        await using var dbContext = CreateSeededContext();

        var province = await dbContext.Set<Province>().SingleAsync(p => p.Name == "Bagmati");
        var district = await dbContext.Set<District>().SingleAsync(d => d.Name == "Kathmandu");
        Assert.Equal(province.Id, district.ProvinceId);

        var localLevel = await dbContext.Set<LocalLevel>().SingleAsync(l => l.Name == "Kathmandu Metropolitan City");
        Assert.Equal(district.Id, localLevel.DistrictId);
        Assert.Equal(LocalLevelType.Metropolitan, localLevel.Type);

        var wards = await dbContext.Set<Ward>().Where(w => w.LocalLevelId == localLevel.Id).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, wards.Select(w => w.Number).OrderBy(n => n));

        var ward1 = wards.Single(w => w.Number == 1);
        var areas = await dbContext.Set<Area>().Where(a => a.WardId == ward1.Id).ToListAsync();
        Assert.Equal(2, areas.Count);
    }

    [Fact]
    public void GeographyIds_AreDeterministicAndUnique()
    {
        var allIds = new List<Guid>();
        foreach (var province in GeographySeedData.Provinces)
        {
            allIds.Add(GeographyIds.ProvinceId(province));
        }
        foreach (var (province, district) in GeographySeedData.Districts)
        {
            allIds.Add(GeographyIds.DistrictId(province, district));
        }
        foreach (var (province, district, localLevel, _) in GeographySeedData.LocalLevels)
        {
            allIds.Add(GeographyIds.LocalLevelId(province, district, localLevel));
        }

        Assert.Equal(allIds.Count, allIds.Distinct().Count());
        Assert.Equal(GeographyIds.ProvinceId("Bagmati"), GeographyIds.ProvinceId("Bagmati"));
    }
}
