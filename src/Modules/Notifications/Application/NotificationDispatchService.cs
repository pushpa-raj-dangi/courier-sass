using System.Text.Json;
using Courier.Merchants.Application;
using Courier.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Notifications.Application;

public class NotificationDispatchService
{
    private readonly DbContext _dbContext;
    private readonly IMerchantContactLookupService _merchantContactLookupService;
    private readonly IEnumerable<INotificationChannel> _channels;

    public NotificationDispatchService(DbContext dbContext, IMerchantContactLookupService merchantContactLookupService, IEnumerable<INotificationChannel> channels)
    {
        _dbContext = dbContext;
        _merchantContactLookupService = merchantContactLookupService;
        _channels = channels;
    }

    // Called once per consumed RabbitMQ message (Task 7). organizationId/eventType/payloadJson
    // come straight from the OutboxMessage row the publisher (Task 4) serialized — this method
    // never touches Shipments/Riders/Ndr/Cod, only what the payload itself carries plus
    // Merchants' contact lookup (Global Constraints).
    public async Task DispatchEventAsync(Guid organizationId, string eventType, string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!NotificationDefaultTemplates.Email.TryGetValue(eventType, out var defaultBody))
        {
            // No default exists for this event type — nothing this phase wires should ever hit
            // this path, but a future event type added to Events without a matching template
            // here would land here rather than throwing, which is the right behavior: silently
            // not-notifying is safer than crashing the consumer over a missing template.
            return;
        }

        var payload = JsonDocument.Parse(payloadJson).RootElement;
        if (!payload.TryGetProperty("MerchantId", out var merchantIdProperty) || !merchantIdProperty.TryGetGuid(out var merchantId))
        {
            throw new NotificationValidationException($"Event '{eventType}' payload has no MerchantId — cannot resolve a recipient.");
        }

        var template = await _dbContext.Set<NotificationTemplate>()
            .Where(t => t.OrganizationId == organizationId && t.EventType == eventType && t.Channel == NotificationChannel.Email && t.IsActive)
            .Select(t => t.Body)
            .FirstOrDefaultAsync(cancellationToken);
        var body = Render(template ?? defaultBody, payload);

        var recipientEmail = await _merchantContactLookupService.GetContactEmailAsync(merchantId, cancellationToken);
        if (recipientEmail is null)
        {
            await WriteLogAsync(organizationId, eventType, null, NotificationStatus.NoRecipient, null, cancellationToken);
            return;
        }

        var emailChannel = _channels.Single(c => c.Channel == NotificationChannel.Email);
        try
        {
            var providerResponse = await emailChannel.SendAsync(recipientEmail, body, cancellationToken);
            await WriteLogAsync(organizationId, eventType, recipientEmail, NotificationStatus.Sent, providerResponse, cancellationToken);
        }
        catch (NotificationDeliveryException ex)
        {
            await WriteLogAsync(organizationId, eventType, recipientEmail, NotificationStatus.Failed, ex.Message, cancellationToken);
        }
    }

    // Every {PropertyName} token in the body is replaced with that property's string value from
    // the payload's top-level JSON, if present — a deliberately simple substitution, not a
    // templating engine, matching CLAUDE.md §3's anti-ceremony guidance.
    private static string Render(string body, JsonElement payload)
    {
        var rendered = body;
        foreach (var property in payload.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
            rendered = rendered.Replace($"{{{property.Name}}}", value);
        }
        return rendered;
    }

    private async Task WriteLogAsync(Guid organizationId, string eventType, string? recipientAddress, NotificationStatus status, string? providerResponse, CancellationToken cancellationToken)
    {
        _dbContext.Set<NotificationLog>().Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EventType = eventType,
            Channel = NotificationChannel.Email,
            RecipientAddress = recipientAddress,
            Status = status,
            SentAt = DateTimeOffset.UtcNow,
            ProviderResponse = providerResponse
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
