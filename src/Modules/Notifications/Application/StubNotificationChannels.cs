using Microsoft.Extensions.Logging;

namespace Courier.Notifications.Application;

// SMS/WhatsApp/Push are not wired to any real provider this phase (spec §2) — each channel
// still exists, is independently unit-testable, and is a real, pluggable implementation of
// INotificationChannel, so a future phase adding a real provider replaces one class, not this
// abstraction. "Sending" here means: log it, never throw — Logged is always the outcome, never
// Sent or Failed, since no real delivery is attempted.
public class SmsNotificationChannel : INotificationChannel
{
    private readonly ILogger<SmsNotificationChannel> _logger;
    public SmsNotificationChannel(ILogger<SmsNotificationChannel> logger) => _logger = logger;
    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.Sms;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SMS (stub, not sent) to {RecipientAddress}: {Body}", recipientAddress, body);
        return Task.FromResult("Logged, no real SMS provider configured");
    }
}

public class WhatsAppNotificationChannel : INotificationChannel
{
    private readonly ILogger<WhatsAppNotificationChannel> _logger;
    public WhatsAppNotificationChannel(ILogger<WhatsAppNotificationChannel> logger) => _logger = logger;
    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.WhatsApp;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WhatsApp (stub, not sent) to {RecipientAddress}: {Body}", recipientAddress, body);
        return Task.FromResult("Logged, no real WhatsApp provider configured");
    }
}

public class PushNotificationChannel : INotificationChannel
{
    private readonly ILogger<PushNotificationChannel> _logger;
    public PushNotificationChannel(ILogger<PushNotificationChannel> logger) => _logger = logger;
    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.Push;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Push (stub, not sent) to {RecipientAddress}: {Body}", recipientAddress, body);
        return Task.FromResult("Logged, no real Push provider configured");
    }
}
