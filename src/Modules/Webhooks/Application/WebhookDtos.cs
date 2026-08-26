namespace Courier.Webhooks.Application;

public record CreateWebhookSubscriptionRequest(string TargetUrl, string[] Events);

// The only response that ever carries the plaintext secret — returned exactly once, at
// creation. Every other read (ListAsync) uses WebhookSubscriptionResponse, which has no secret
// field at all.
public record CreateWebhookSubscriptionResponse(Guid Id, string TargetUrl, string[] Events, bool IsActive, string Secret);

public record WebhookSubscriptionResponse(Guid Id, string TargetUrl, string[] Events, bool IsActive);

public record WebhookDeliveryResponse(
    Guid Id, Guid WebhookSubscriptionId, string EventType, int Attempt, int? ResponseStatus,
    string? ResponseSnippet, DateTimeOffset? DeliveredAt, DateTimeOffset? NextRetryAt);

public class WebhookValidationException : Exception
{
    public WebhookValidationException(string message) : base(message) { }
}
