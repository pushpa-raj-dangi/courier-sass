namespace Courier.Events.Application;

// Thin seam over the actual RabbitMQ publish call — exists purely so OutboxPublisherJob's
// polling/marking logic can be unit-tested against a fake, the same reason
// Notifications.Application.INotificationChannel (Task 6) exists as an abstraction over the
// real SMTP call.
public interface IEventPublisher
{
    Task PublishAsync(string exchange, string routingKey, string payload, CancellationToken cancellationToken = default);
}
