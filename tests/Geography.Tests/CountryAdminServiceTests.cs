using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

file class RecordingAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = new();
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

public class CountryAdminServiceTests
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
    public async Task UpdateAsync_TogglesIsServiced_AndAudits()
    {
        await using var dbContext = CreateSeededContext();
        var audit = new RecordingAuditLogger();
        var service = new CountryAdminService(dbContext, audit);
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");
        var actingUserId = Guid.NewGuid();

        var result = await service.UpdateAsync(india.Id, new UpdateCountryRequest(true, null), actingUserId);

        Assert.NotNull(result);
        Assert.True(result!.IsServiced);
        Assert.True((await dbContext.Set<Country>().SingleAsync(c => c.Id == india.Id)).IsServiced);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("country.updated", entry.Action);
        Assert.Null(entry.OrganizationId);
        Assert.True(entry.IsCrossTenantAction);
    }

    [Fact]
    public async Task UpdateAsync_AssignsZone_AndReturnsZoneName()
    {
        await using var dbContext = CreateSeededContext();
        var zone = new CountryZone { Id = Guid.NewGuid(), Name = "SAARC", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<CountryZone>().Add(zone);
        await dbContext.SaveChangesAsync();
        var service = new CountryAdminService(dbContext, new RecordingAuditLogger());
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");

        var result = await service.UpdateAsync(india.Id, new UpdateCountryRequest(true, zone.Id), Guid.NewGuid());

        Assert.Equal("SAARC", result!.CountryZoneName);
    }

    [Fact]
    public async Task UpdateAsync_UnknownZoneId_ThrowsValidationException()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryAdminService(dbContext, new RecordingAuditLogger());
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");

        await Assert.ThrowsAsync<CountryValidationException>(() =>
            service.UpdateAsync(india.Id, new UpdateCountryRequest(true, Guid.NewGuid()), Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_UnknownCountryId_ReturnsNull()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryAdminService(dbContext, new RecordingAuditLogger());

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateCountryRequest(true, null), Guid.NewGuid());

        Assert.Null(result);
    }
}
