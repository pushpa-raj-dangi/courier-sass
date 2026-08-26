namespace Courier.Notifications.Application;

public class NotificationDeliveryException : Exception
{
    public NotificationDeliveryException(string message) : base(message) { }
    public NotificationDeliveryException(string message, Exception innerException) : base(message, innerException) { }
}

// One implementation per Domain.NotificationChannel value (EmailNotificationChannel /
// StubNotificationChannels) — NotificationDispatchService resolves the right one from an
// injected IEnumerable<INotificationChannel> by matching .Channel. SendAsync returns a short
// provider-response string on success (stored verbatim in NotificationLog.ProviderResponse) and
// throws NotificationDeliveryException on failure — the dispatch service never needs to know
// which concrete channel it's talking to.
public interface INotificationChannel
{
    Courier.Notifications.Domain.NotificationChannel Channel { get; }
    Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default);
}
