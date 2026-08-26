namespace Courier.SharedKernel;

// One topic exchange, shared by the outbox publisher (Events, Task 4) and both consumers
// (Notifications Task 7, Webhooks Task 11) — living in SharedKernel (referenced by every
// module already) is what lets all three agree on the exchange/queue names without any of
// them referencing each other.
public static class RabbitMqTopology
{
    public const string DomainEventsExchange = "courier.domain-events";
    public const string NotificationsQueue = "courier.notifications";
    public const string WebhooksQueue = "courier.webhooks";
}
