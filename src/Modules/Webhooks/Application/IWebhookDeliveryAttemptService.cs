namespace Courier.Webhooks.Application;

// One method, reused by three callers: WebhooksConsumer (first attempt, immediately after
// creating a WebhookDelivery row), WebhookRetryJob (subsequent attempts, on its own schedule),
// and WebhookService.ReplayAsync (on demand). Every caller already knows the delivery exists —
// this only takes its id, loads everything else itself.
public interface IWebhookDeliveryAttemptService
{
    Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}
