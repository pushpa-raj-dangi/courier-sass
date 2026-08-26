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
    // A real reversible round-trip, no external Data Protection dependency needed in unit
    // tests — just enough to prove WebhookService never stores the plaintext.
    public string Protect(string plaintext) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("protected:" + plaintext));
    public string Unprotect(string ciphertext) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext))["protected:".Length..];
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class WebhookServiceTests
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
    public async Task CreateSubscriptionAsync_ReturnsPlaintextSecretOnce_StoresOnlyCiphertext()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var response = await service.CreateSubscriptionAsync(organizationId, merchantId, new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));

        Assert.NotEmpty(response.Secret);
        var stored = await dbContext.Set<WebhookSubscription>().SingleAsync();
        Assert.NotEqual(response.Secret, stored.Secret);
        Assert.Equal(response.Secret, new InMemorySecretProtector().Unprotect(stored.Secret));
    }

    [Fact]
    public async Task CreateSubscriptionAsync_UnknownEventType_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<WebhookValidationException>(() =>
            service.CreateSubscriptionAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "not.a.real.event" })));
    }

    [Fact]
    public async Task CreateSubscriptionAsync_InvalidUrl_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<WebhookValidationException>(() =>
            service.CreateSubscriptionAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateWebhookSubscriptionRequest("not-a-url", new[] { "shipment.created" })));
    }

    [Fact]
    public async Task ListAsync_OnlyReturnsCallingMerchantsOwnSubscriptions()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var service = CreateService(dbContext);
        await service.CreateSubscriptionAsync(organizationId, merchantId, new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        await service.CreateSubscriptionAsync(organizationId, Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://other.example.com/hook", new[] { "shipment.created" }));

        var result = await service.ListAsync(organizationId, merchantId);

        Assert.Single(result);
    }

    [Fact]
    public async Task DeleteAsync_ForeignMerchantsSubscription_ReturnsFalseAndDoesNotDelete()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext);
        var created = await service.CreateSubscriptionAsync(organizationId, Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));

        var deleted = await service.DeleteAsync(organizationId, Guid.NewGuid(), created.Id);

        Assert.False(deleted);
        Assert.Equal(1, await dbContext.Set<WebhookSubscription>().CountAsync());
    }
}
