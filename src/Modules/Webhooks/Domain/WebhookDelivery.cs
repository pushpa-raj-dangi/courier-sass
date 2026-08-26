namespace Courier.Webhooks.Domain;

// One row per (subscription, source event) pair the Webhooks consumer (Task 11) creates, plus
// however many retry attempts the retry job (Task 12) records against the SAME row (Attempt
// increments in place — this is not one row per attempt, it is one row per delivery, whose
// Attempt/ResponseStatus/ResponseSnippet/NextRetryAt fields are overwritten on each retry, only
// DeliveredAt ever being a terminal, no-longer-overwritten field once set).
public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WebhookSubscriptionId { get; set; }
    public Guid SourceEventId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public int Attempt { get; set; }
    public int? ResponseStatus { get; set; }
    public string? ResponseSnippet { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
}
