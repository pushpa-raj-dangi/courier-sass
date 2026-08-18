using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class DeliveryZoneServiceTests
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

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static List<Guid> KathmanduWard1Areas() =>
        new()
        {
            GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
            GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
        };

    [Fact]
    public async Task CreateAsync_ValidAreas_PersistsZoneWithCoverage()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger());

        var result = await service.CreateAsync(
            new CreateDeliveryZoneRequest("Kathmandu Ward 1", KathmanduWard1Areas()),
            ActingUser(Guid.NewGuid()));

        Assert.Equal("Kathmandu Ward 1", result.Name);
        Assert.Equal(2, result.CoveredAreaIds.Count);
    }

    [Fact]
    public async Task CreateAsync_UnknownAreaId_ThrowsValidationException()
    {
        await using var dbContext = CreateSeededContext();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger());

        await Assert.ThrowsAsync<DeliveryZoneValidationException>(() =>
            service.CreateAsync(
                new CreateDeliveryZoneRequest("Bad Zone", new List<Guid> { Guid.NewGuid() }),
                ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateAsync_NoAreas_ThrowsValidationException()
    {
        await using var dbContext = CreateSeededContext();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger());

        await Assert.ThrowsAsync<DeliveryZoneValidationException>(() =>
            service.CreateAsync(
                new CreateDeliveryZoneRequest("Empty Zone", new List<Guid>()),
                ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetAsync_ThenUpdateAsync_ChangesNameAndCoverage()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger());
        var created = await service.CreateAsync(
            new CreateDeliveryZoneRequest("Original Name", KathmanduWard1Areas()),
            ActingUser(Guid.NewGuid()));

        var ward2Areas = new List<Guid>
        {
            GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 2, "Area A"),
        };
        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateDeliveryZoneRequest("Renamed", ward2Areas),
            ActingUser(Guid.NewGuid()));

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(ward2Areas, updated.CoveredAreaIds);
    }

    [Fact]
    public async Task ListAsync_ReturnsCreatedZone()
    {
        await using var dbContext = CreateSeededContext();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger());
        await service.CreateAsync(new CreateDeliveryZoneRequest("Listed Zone", KathmanduWard1Areas()), ActingUser(Guid.NewGuid()));

        var zones = await service.ListAsync();

        Assert.Contains(zones, z => z.Name == "Listed Zone");
    }
}
