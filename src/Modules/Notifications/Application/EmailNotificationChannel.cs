using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace Courier.Notifications.Application;

// The one real channel this phase — connects to whatever SMTP host/port appsettings' Smtp
// section names (Task 8 points this at a dev-only mail-catcher container in docker-compose;
// production would point it at a real SMTP relay via the same config keys, no code change).
public class EmailNotificationChannel : INotificationChannel
{
    private readonly IConfiguration _configuration;

    public EmailNotificationChannel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.Email;

    public async Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"] ?? throw new NotificationDeliveryException("Smtp:Host is not configured.");
        var port = int.Parse(_configuration["Smtp:Port"] ?? throw new NotificationDeliveryException("Smtp:Port is not configured."));
        var from = _configuration["Smtp:From"] ?? "noreply@courier.local";

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(recipientAddress));
        message.Subject = "Courier notification";
        message.Body = new TextPart("plain") { Text = body };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.None, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return $"Delivered to {host}:{port}";
        }
        catch (Exception ex) when (ex is not NotificationDeliveryException)
        {
            throw new NotificationDeliveryException($"SMTP send failed: {ex.Message}", ex);
        }
    }
}
