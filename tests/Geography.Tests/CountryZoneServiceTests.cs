using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CountryZoneServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task CreateAsync_PersistsZone()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());

        var result = await service.CreateAsync(new CreateCountryZoneRequest("SAARC", "South Asia"), Guid.NewGuid());

        Assert.Equal("SAARC", result.Name);
        Assert.Equal(1, await dbContext.Set<CountryZone>().CountAsync());
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        await service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid());

        await Assert.ThrowsAsync<CountryZoneValidationException>(() =>
            service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_BlankName_Throws()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());

        await Assert.ThrowsAsync<CountryZoneValidationException>(() =>
            service.CreateAsync(new CreateCountryZoneRequest("  ", null), Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_RenamesZone()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        var created = await service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid());

        var updated = await service.UpdateAsync(created.Id, new UpdateCountryZoneRequest("South Asia", "Renamed"), Guid.NewGuid());

        Assert.Equal("South Asia", updated!.Name);
        Assert.Equal("Renamed", updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateNameAgainstAnotherZone_Throws()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        await service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid());
        var second = await service.CreateAsync(new CreateCountryZoneRequest("Middle East", null), Guid.NewGuid());

        await Assert.ThrowsAsync<CountryZoneValidationException>(() =>
            service.UpdateAsync(second.Id, new UpdateCountryZoneRequest("SAARC", null), Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateCountryZoneRequest("X", null), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_OrdersByName()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        await service.CreateAsync(new CreateCountryZoneRequest("Middle East", null), Guid.NewGuid());
        await service.CreateAsync(new CreateCountryZoneRequest("Asia-Pacific", null), Guid.NewGuid());

        var zones = await service.ListAsync();

        Assert.Equal(new[] { "Asia-Pacific", "Middle East" }, zones.Select(z => z.Name));
    }
}
