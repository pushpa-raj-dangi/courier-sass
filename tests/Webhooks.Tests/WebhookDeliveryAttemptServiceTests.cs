using System.Net;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Webhooks.Tests;

file class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
}

file class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;
    public string? LastSignatureHeader { get; private set; }

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody = "")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastSignatureHeader = request.Headers.TryGetValues("X-Webhook-Signature", out var values) ? values.First() : null;
        return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody) });
    }
}

file class SingleClientHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;
    public SingleClientHttpClientFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);
    public HttpClient CreateClient(string name) => _client;
}

public class WebhookDeliveryAttemptServiceTests
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

    private static async Task<(WebhookSubscription Subscription, WebhookDelivery Delivery)> SeedAsync(TestDbContext dbContext, int initialAttempt = 0)
    {
        var subscription = new WebhookSubscription { Id = Guid.NewGuid(), MerchantId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), TargetUrl = "https://example.com/hook", Secret = "test-secret", Events = new[] { "shipment.created" }, IsActive = true };
        var delivery = new WebhookDelivery { Id = Guid.NewGuid(), WebhookSubscriptionId = subscription.Id, SourceEventId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{\"a\":1}", Attempt = initialAttempt };
        dbContext.Set<WebhookSubscription>().Add(subscription);
        dbContext.Set<WebhookDelivery>().Add(delivery);
        await dbContext.SaveChangesAsync();
        return (subscription, delivery);
    }

    [Fact]
    public async Task AttemptAsync_SuccessResponse_SetsDeliveredAtAndClearsNextRetry()
    {
        await using var dbContext = CreateContext();
        var (_, delivery) = await SeedAsync(dbContext);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var service = new WebhookDeliveryAttemptService(dbContext, new FakeSecretProtector(), new SingleClientHttpClientFactory(handler));

        await service.AttemptAsync(delivery.Id);

        var updated = await dbContext.Set<WebhookDelivery>().SingleAsync();
        Assert.NotNull(updated.DeliveredAt);
        Assert.Null(updated.NextRetryAt);
        Assert.Equal(200, updated.ResponseStatus);
        Assert.Equal(1, updated.Attempt);
        Assert.NotNull(handler.LastSignatureHeader);
        Assert.StartsWith("sha256=", handler.LastSignatureHeader);
    }

    [Fact]
    public async Task AttemptAsync_FailureResponse_BelowMaxAttempts_SchedulesBackoffRetry()
    {
        await using var dbContext = CreateContext();
        var (_, delivery) = await SeedAsync(dbContext, initialAttempt: 1);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "boom");
        var service = new WebhookDeliveryAttemptService(dbContext, new FakeSecretProtector(), new SingleClientHttpClientFactory(handler));

        await service.AttemptAsync(delivery.Id);

        var updated = await dbContext.Set<WebhookDelivery>().SingleAsync();
        Assert.Null(updated.DeliveredAt);
        Assert.NotNull(updated.NextRetryAt);
        Assert.Equal(2, updated.Attempt);
        // Attempt 2's backoff is 2^(2-1) = 2 minutes.
        Assert.True(updated.NextRetryAt > DateTimeOffset.UtcNow.AddMinutes(1.9) && updated.NextRetryAt < DateTimeOffset.UtcNow.AddMinutes(2.1));
    }

    [Fact]
    public async Task AttemptAsync_FailureResponse_AtMaxAttempts_LeavesTerminalWithNoRetry()
    {
        await using var dbContext = CreateContext();
        var (_, delivery) = await SeedAsync(dbContext, initialAttempt: 4);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "boom");
        var service = new WebhookDeliveryAttemptService(dbContext, new FakeSecretProtector(), new SingleClientHttpClientFactory(handler));

        await service.AttemptAsync(delivery.Id);

        var updated = await dbContext.Set<WebhookDelivery>().SingleAsync();
        Assert.Equal(5, updated.Attempt);
        Assert.Null(updated.DeliveredAt);
        Assert.Null(updated.NextRetryAt);
    }
}
