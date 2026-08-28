using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Webhooks.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class InMemorySecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class RecordingAttemptService : IWebhookDeliveryAttemptService
{
    public List<Guid> Attempted { get; } = new();
    public Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        Attempted.Add(deliveryId);
        return Task.CompletedTask;
    }
}

public class WebhookServiceReplayTests
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

    private static WebhookService CreateService(TestDbContext dbContext) =>
        new(dbContext, new NoopAuditLogger(), new InMemorySecretProtector(), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task ReplayAsync_ResetsAttemptCounterBeforeCallingAttemptService()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var service = CreateService(dbContext);
        var subscription = await service.CreateSubscriptionAsync(organizationId, merchantId, new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        var delivery = new WebhookDelivery { Id = Guid.NewGuid(), WebhookSubscriptionId = subscription.Id, SourceEventId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", Attempt = 5, ResponseStatus = 500, NextRetryAt = null };
        dbContext.Set<WebhookDelivery>().Add(delivery);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();

        var result = await service.ReplayAsync(organizationId, merchantId, delivery.Id, attemptService);

        Assert.NotNull(result);
        Assert.Single(attemptService.Attempted, id => id == delivery.Id);
    }

    [Fact]
    public async Task ReplayAsync_ForeignMerchantsDelivery_ReturnsNullAndDoesNotAttempt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext);
        var subscription = await service.CreateSubscriptionAsync(organizationId, Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        var delivery = new WebhookDelivery { Id = Guid.NewGuid(), WebhookSubscriptionId = subscription.Id, SourceEventId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", Attempt = 1 };
        dbContext.Set<WebhookDelivery>().Add(delivery);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();

        var result = await service.ReplayAsync(organizationId, Guid.NewGuid(), delivery.Id, attemptService);

        Assert.Null(result);
        Assert.Empty(attemptService.Attempted);
    }
}
