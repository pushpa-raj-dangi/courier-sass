using System.Text.Json;
using Courier.Merchants.Application;
using Courier.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Notifications.Application;

public class NotificationDispatchService
{
    private readonly DbContext _dbContext;
    private readonly IMerchantContactLookupService _merchantContactLookupService;
    private readonly IOrganizationLocaleLookup _organizationLocaleLookup;
    private readonly IEnumerable<INotificationChannel> _channels;

    public NotificationDispatchService(DbContext dbContext, IMerchantContactLookupService merchantContactLookupService, IOrganizationLocaleLookup organizationLocaleLookup, IEnumerable<INotificationChannel> channels)
    {
        _dbContext = dbContext;
        _merchantContactLookupService = merchantContactLookupService;
        _organizationLocaleLookup = organizationLocaleLookup;
        _channels = channels;
    }

    // Called once per consumed RabbitMQ message (Task 7). organizationId/eventType/payloadJson
    // come straight from the OutboxMessage row the publisher (Task 4) serialized — this method
    // never touches Shipments/Riders/Ndr/Cod, only what the payload itself carries plus
    // Merchants' contact lookup (Global Constraints).
    public async Task DispatchEventAsync(Guid organizationId, string eventType, string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!NotificationDefaultTemplates.IsKnownEvent(eventType))
        {
            // No default exists for this event type — nothing this phase wires should ever hit
            // this path, but a future event type added to Events without a matching template
            // here would land here rather than throwing, which is the right behavior: silently
            // not-notifying is safer than crashing the consumer over a missing template.
            return;
        }

        var payload = JsonDocument.Parse(payloadJson).RootElement;

        // Two ways an event can name its recipient: a self-contained RecipientEmail (user.invited
        // — the invitee isn't a merchant, there's nothing to look up) or, for every other existing
        // event, a MerchantId this method resolves via IMerchantContactLookupService as before.
        // Checked first and independently so a RecipientEmail payload never needs a MerchantId at
        // all, and an existing MerchantId-keyed payload is completely unaffected by this branch.
        string? recipientEmail;
        if (payload.TryGetProperty("RecipientEmail", out var recipientEmailProperty) && recipientEmailProperty.ValueKind == JsonValueKind.String)
        {
            recipientEmail = recipientEmailProperty.GetString();
        }
        else
        {
            if (!payload.TryGetProperty("MerchantId", out var merchantIdProperty) || !merchantIdProperty.TryGetGuid(out var merchantId))
            {
                throw new NotificationValidationException($"Event '{eventType}' payload has no RecipientEmail or MerchantId — cannot resolve a recipient.");
            }
            recipientEmail = await _merchantContactLookupService.GetContactEmailAsync(organizationId, merchantId, cancellationToken);
        }

        var locale = NotificationLocales.Normalize(await _organizationLocaleLookup.GetDefaultLocaleAsync(organizationId, cancellationToken));

        // IgnoreQueryFilters() is required here, not optional: this method's real caller
        // (NotificationsConsumer) runs with no ICurrentTenantProvider context at all (a
        // background service, not an HTTP request), so the tenant query filter's
        // `_currentTenantProvider.OrganizationId != null && ...` guard would evaluate false for
        // every row, silently hiding any org-specific override template — not a crash, just a
        // template override that quietly never takes effect. The explicit .OrganizationId ==
        // organizationId filter below is what actually scopes this read; bypassing the ambient
        // filter doesn't weaken that, it only fixes the no-tenant-context caller (same bug shape
        // and fix as Webhooks.Infrastructure.WebhookDeliveryAttemptService.AttemptAsync).
        var overrides = await _dbContext.Set<NotificationTemplate>().IgnoreQueryFilters()
            .Where(t => t.OrganizationId == organizationId && t.EventType == eventType && t.Channel == NotificationChannel.Email && t.IsActive)
            .Select(t => new { t.Locale, t.Body })
            .ToListAsync(cancellationToken);
        var bodiesByLocale = overrides
            .GroupBy(t => t.Locale)
            .ToDictionary(g => g.Key, g => g.First().Body);
        var body = Render(NotificationTemplateResolver.Resolve(locale, eventType, bodiesByLocale)!, payload);

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
