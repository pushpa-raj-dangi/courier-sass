using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class GeographyQueryServiceTests
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
            modelBuilder.ApplyConfiguration(new DeliveryZoneConfiguration());
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
    public async Task ListProvincesAsync_ReturnsAllSevenOrderedByName()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        var provinces = await service.ListProvincesAsync();

        Assert.Equal(7, provinces.Count);
        Assert.Contains(provinces, p => p.Name == "Bagmati");
        Assert.Equal(provinces.OrderBy(p => p.Name).Select(p => p.Name), provinces.Select(p => p.Name));
    }

    [Fact]
    public async Task ListDistrictsAsync_FiltersByProvince()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var bagmatiId = GeographyIds.ProvinceId("Bagmati");

        var districts = await service.ListDistrictsAsync(bagmatiId);

        Assert.Equal(13, districts.Count);
        Assert.Contains(districts, d => d.Name == "Kathmandu");
        Assert.All(districts, d => Assert.Equal(bagmatiId, d.ProvinceId));
    }

    [Fact]
    public async Task ListLocalLevelsAsync_FiltersByDistrict()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var kathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");

        var localLevels = await service.ListLocalLevelsAsync(kathmanduDistrictId);

        var localLevel = Assert.Single(localLevels);
        Assert.Equal("Kathmandu Metropolitan City", localLevel.Name);
        Assert.Equal(LocalLevelType.Metropolitan, localLevel.Type);
    }

    [Fact]
    public async Task ListWardsAsync_ReturnsThreeWardsOrderedByNumber()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var kathmanduMetroId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

        var wards = await service.ListWardsAsync(kathmanduMetroId);

        Assert.Equal(new[] { 1, 2, 3 }, wards.Select(w => w.Number));
    }

    [Fact]
    public async Task ListAreasAsync_ReturnsTwoAreasForWard()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var ward1Id = GeographyIds.WardId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1);

        var areas = await service.ListAreasAsync(ward1Id);

        Assert.Equal(2, areas.Count);
    }

    [Fact]
    public async Task IsValidHierarchyAsync_CorrectChain_ReturnsTrue()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        var result = await service.IsValidHierarchyAsync(
            GeographyIds.ProvinceId("Bagmati"),
            GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber: 2);

        Assert.True(result);
    }

    [Fact]
    public async Task IsValidHierarchyAsync_LocalLevelFromDifferentDistrict_ReturnsFalse()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        // Kathmandu's province/district paired with Pokhara's local level — mismatched chain.
        var result = await service.IsValidHierarchyAsync(
            GeographyIds.ProvinceId("Bagmati"),
            GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            GeographyIds.LocalLevelId("Gandaki", "Kaski", "Pokhara Metropolitan City"),
            wardNumber: 1);

        Assert.False(result);
    }

    [Fact]
    public async Task IsValidHierarchyAsync_WardNumberNotSeeded_ReturnsFalse()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        var result = await service.IsValidHierarchyAsync(
            GeographyIds.ProvinceId("Bagmati"),
            GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber: 99);

        Assert.False(result);
    }

    [Fact]
    public async Task DeliveryZoneExistsAsync_ZoneInCurrentOrg_ReturnsTrue()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var zone = new DeliveryZone { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Zone A" };
        dbContext.Set<DeliveryZone>().Add(zone);
        await dbContext.SaveChangesAsync();

        var service = new GeographyQueryService(dbContext);

        Assert.True(await service.DeliveryZoneExistsAsync(zone.Id));
    }

    [Fact]
    public async Task DeliveryZoneExistsAsync_UnknownZoneId_ReturnsFalse()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        Assert.False(await service.DeliveryZoneExistsAsync(Guid.NewGuid()));
    }
}
