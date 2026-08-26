using System.Net.Http.Json;
using System.Text;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Infrastructure;

public class WebhookDeliveryAttemptService : IWebhookDeliveryAttemptService
{
    private readonly DbContext _dbContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IHttpClientFactory _httpClientFactory;

    // 1m, 2m, 4m, 8m, 16m for attempts 1-5 (Global Constraints) — attempt N's backoff is
    // 2^(N-1) minutes; after attempt 5 fails, NextRetryAt stays null (terminal).
    private const int MaxAttempts = 5;
    private static TimeSpan BackoffFor(int attempt) => TimeSpan.FromMinutes(Math.Pow(2, attempt - 1));

    public WebhookDeliveryAttemptService(DbContext dbContext, ISecretProtector secretProtector, IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _secretProtector = secretProtector;
        _httpClientFactory = httpClientFactory;
    }

    public async Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.Set<WebhookDelivery>()
            .Join(_dbContext.Set<WebhookSubscription>(), d => d.WebhookSubscriptionId, s => s.Id, (d, s) => new { Delivery = d, Subscription = s })
            .FirstOrDefaultAsync(x => x.Delivery.Id == deliveryId, cancellationToken);
        if (result is null)
        {
            return;
        }
        var (delivery, subscription) = (result.Delivery, result.Subscription);

        delivery.Attempt += 1;
        var secret = _secretProtector.Unprotect(subscription.Secret);
        var signature = WebhookSignature.Compute(secret, delivery.Payload);

        var client = _httpClientFactory.CreateClient("Webhooks");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.TargetUrl)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Webhook-Signature", signature);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            delivery.ResponseStatus = (int)response.StatusCode;
            delivery.ResponseSnippet = body.Length > 500 ? body[..500] : body;

            if (response.IsSuccessStatusCode)
            {
                delivery.DeliveredAt = DateTimeOffset.UtcNow;
                delivery.NextRetryAt = null;
            }
            else
            {
                delivery.NextRetryAt = delivery.Attempt < MaxAttempts ? DateTimeOffset.UtcNow + BackoffFor(delivery.Attempt) : null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            delivery.ResponseStatus = null;
            delivery.ResponseSnippet = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            delivery.NextRetryAt = delivery.Attempt < MaxAttempts ? DateTimeOffset.UtcNow + BackoffFor(delivery.Attempt) : null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
