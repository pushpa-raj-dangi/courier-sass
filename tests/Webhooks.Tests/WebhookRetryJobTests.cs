using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Webhooks.Tests;

file class RecordingAttemptService : IWebhookDeliveryAttemptService
{
    public List<Guid> Attempted { get; } = new();
    public Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        Attempted.Add(deliveryId);
        return Task.CompletedTask;
    }
}

public class WebhookRetryJobTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
            modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static WebhookDelivery MakeDelivery(DateTimeOffset? nextRetryAt) => new()
    {
        Id = Guid.NewGuid(), WebhookSubscriptionId = Guid.NewGuid(), SourceEventId = Guid.NewGuid(),
        EventType = "shipment.created", Payload = "{}", Attempt = 1, NextRetryAt = nextRetryAt
    };

    [Fact]
    public async Task RunAsync_DueDelivery_AttemptsIt()
    {
        await using var dbContext = CreateContext();
        var due = MakeDelivery(DateTimeOffset.UtcNow.AddMinutes(-1));
        dbContext.Set<WebhookDelivery>().Add(due);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();
        var job = new WebhookRetryJob(dbContext, attemptService);

        await job.RunAsync();

        Assert.Single(attemptService.Attempted, id => id == due.Id);
    }

    [Fact]
    public async Task RunAsync_NotYetDueDelivery_DoesNotAttemptIt()
    {
        await using var dbContext = CreateContext();
        var notYetDue = MakeDelivery(DateTimeOffset.UtcNow.AddMinutes(5));
        dbContext.Set<WebhookDelivery>().Add(notYetDue);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();
        var job = new WebhookRetryJob(dbContext, attemptService);

        await job.RunAsync();

        Assert.Empty(attemptService.Attempted);
    }

    [Fact]
    public async Task RunAsync_DeliveryWithNoNextRetryAt_DoesNotAttemptIt()
    {
        // Either terminal-failed (5 attempts exhausted) or already-delivered — either way,
        // NextRetryAt is null and this job must never touch it.
        await using var dbContext = CreateContext();
        var terminal = MakeDelivery(null);
        dbContext.Set<WebhookDelivery>().Add(terminal);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();
        var job = new WebhookRetryJob(dbContext, attemptService);

        await job.RunAsync();

        Assert.Empty(attemptService.Attempted);
    }
}
