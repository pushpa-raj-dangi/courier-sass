using Courier.Events.Application;
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Events.Tests;

file class RecordingEventPublisher : IEventPublisher
{
    public List<(string Exchange, string RoutingKey, string Payload)> Published { get; } = new();
    public Task PublishAsync(string exchange, string routingKey, string payload, CancellationToken cancellationToken = default)
    {
        Published.Add((exchange, routingKey, payload));
        return Task.CompletedTask;
    }
}

public class OutboxPublisherJobTests
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
    public async Task RunAsync_PublishesUnpublishedRowsAndMarksThemPublished()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var row = new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = "shipment.created", Payload = "{\"ShipmentId\":\"11111111-1111-1111-1111-111111111111\"}", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 };
        dbContext.Set<OutboxMessage>().Add(row);
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingEventPublisher();
        var job = new OutboxPublisherJob(dbContext, publisher);

        await job.RunAsync();

        var updated = await dbContext.Set<OutboxMessage>().SingleAsync();
        Assert.NotNull(updated.PublishedAt);
        Assert.Equal(1, updated.Attempts);
        var published = Assert.Single(publisher.Published, p => p.RoutingKey == "shipment.created");
        Assert.Contains(row.Id.ToString(), published.Payload);
        Assert.Contains(organizationId.ToString(), published.Payload);
        Assert.Contains("11111111-1111-1111-1111-111111111111", published.Payload);
    }

    [Fact]
    public async Task RunAsync_SkipsAlreadyPublishedRows()
    {
        await using var dbContext = CreateContext();
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-5), Attempts = 1 });
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingEventPublisher();
        var job = new OutboxPublisherJob(dbContext, publisher);

        await job.RunAsync();

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task RunAsync_MultipleOrganizations_PublishesAcrossAllOfThem()
    {
        // The job runs with no tenant context (a background job, not a request) — this proves
        // IgnoreQueryFilters() is actually doing its job, not silently limiting to one org.
        await using var dbContext = CreateContext();
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 });
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "cod.collected", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 });
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingEventPublisher();
        var job = new OutboxPublisherJob(dbContext, publisher);

        await job.RunAsync();

        Assert.Equal(2, publisher.Published.Count);
    }
}
