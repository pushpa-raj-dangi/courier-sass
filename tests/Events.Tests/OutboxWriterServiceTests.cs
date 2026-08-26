using Courier.Events.Application;
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Events.Tests;

public class OutboxWriterServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
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
    public async Task WriteAsync_AddsUnpublishedRowWithSerializedPayload()
    {
        await using var dbContext = CreateContext();
        var writer = new OutboxWriterService(dbContext);
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        await writer.WriteAsync(organizationId, "shipment.created", new { ShipmentId = shipmentId, MerchantId = merchantId, Awb = "TEST123" });
        await dbContext.SaveChangesAsync();

        var row = await dbContext.Set<OutboxMessage>().SingleAsync();
        Assert.Equal(organizationId, row.OrganizationId);
        Assert.Equal("shipment.created", row.EventType);
        Assert.Null(row.PublishedAt);
        Assert.Equal(0, row.Attempts);
        Assert.Contains(shipmentId.ToString(), row.Payload);
        Assert.Contains(merchantId.ToString(), row.Payload);
        Assert.Contains("TEST123", row.Payload);
    }

    [Fact]
    public async Task WriteAsync_DoesNotCallSaveChangesItself()
    {
        // Proves the "same transaction as the caller" design: WriteAsync alone, with no
        // subsequent SaveChangesAsync, must NOT persist anything — only the caller's own
        // SaveChangesAsync (simulated here) commits it.
        await using var dbContext = CreateContext();
        var writer = new OutboxWriterService(dbContext);

        await writer.WriteAsync(Guid.NewGuid(), "shipment.created", new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid() });

        Assert.Empty(await dbContext.Set<OutboxMessage>().ToListAsync());
        Assert.Single(dbContext.ChangeTracker.Entries<OutboxMessage>());
    }
}
