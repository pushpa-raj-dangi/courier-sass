using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Application;

// Declared partial: ReplayAsync lives in WebhookService.Replay.cs (Task 11), added once
// IWebhookDeliveryAttemptService exists — adding it here would make this file fail to compile
// before that interface exists, breaking "each task ends with an independently testable
// deliverable."
public partial class WebhookService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly ISecretProtector _secretProtector;
    private readonly IMerchantLookupService _merchantLookupService;

    private static readonly HashSet<string> ValidEventTypes = new()
    {
        "shipment.created", "shipment.picked_up", "shipment.in_transit", "shipment.out_for_delivery",
        "shipment.delivered", "shipment.delivery_failed", "shipment.ndr_created", "shipment.rto",
        "cod.collected", "cod.settled"
    };

    public WebhookService(DbContext dbContext, IAuditLogger auditLogger, ISecretProtector secretProtector, IMerchantLookupService merchantLookupService)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _secretProtector = secretProtector;
        _merchantLookupService = merchantLookupService;
    }

    public async Task<CreateWebhookSubscriptionResponse> CreateSubscriptionAsync(Guid organizationId, Guid merchantId, CreateWebhookSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _merchantLookupService.ExistsAsync(merchantId, cancellationToken))
        {
            throw new WebhookValidationException("Merchant was not found in this organization.");
        }
        if (!Uri.TryCreate(request.TargetUrl, UriKind.Absolute, out var targetUri) || (targetUri.Scheme != "http" && targetUri.Scheme != "https"))
        {
            throw new WebhookValidationException("TargetUrl must be an absolute http/https URL.");
        }
        if (request.Events.Length == 0)
        {
            throw new WebhookValidationException("At least one event type is required.");
        }
        var invalidEvents = request.Events.Where(e => !ValidEventTypes.Contains(e)).ToArray();
        if (invalidEvents.Length > 0)
        {
            throw new WebhookValidationException($"Unknown event type(s): {string.Join(", ", invalidEvents)}");
        }

        var plaintextSecret = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            TargetUrl = request.TargetUrl,
            Secret = _secretProtector.Protect(plaintextSecret),
            Events = request.Events,
            IsActive = true
        };
        _dbContext.Set<WebhookSubscription>().Add(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "webhook.subscription_created", "WebhookSubscription", subscription.Id.ToString()), cancellationToken);
        return new CreateWebhookSubscriptionResponse(subscription.Id, subscription.TargetUrl, subscription.Events, subscription.IsActive, plaintextSecret);
    }

    public async Task<List<WebhookSubscriptionResponse>> ListAsync(Guid organizationId, Guid merchantId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _dbContext.Set<WebhookSubscription>()
            .Where(s => s.OrganizationId == organizationId && s.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return subscriptions.Select(ToResponse).ToList();
    }

    public async Task<bool> DeleteAsync(Guid organizationId, Guid merchantId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.Set<WebhookSubscription>()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.OrganizationId == organizationId && s.MerchantId == merchantId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        _dbContext.Set<WebhookSubscription>().Remove(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "webhook.subscription_deleted", "WebhookSubscription", subscription.Id.ToString()), cancellationToken);
        return true;
    }

    public async Task<List<WebhookDeliveryResponse>?> ListDeliveriesAsync(Guid organizationId, Guid merchantId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var owned = await _dbContext.Set<WebhookSubscription>()
            .AnyAsync(s => s.Id == subscriptionId && s.OrganizationId == organizationId && s.MerchantId == merchantId, cancellationToken);
        if (!owned)
        {
            return null;
        }

        var deliveries = await _dbContext.Set<WebhookDelivery>()
            .Where(d => d.WebhookSubscriptionId == subscriptionId)
            .OrderByDescending(d => d.DeliveredAt ?? DateTimeOffset.MaxValue)
            .ToListAsync(cancellationToken);
        return deliveries.Select(ToDeliveryResponse).ToList();
    }

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription subscription) => new(
        subscription.Id, subscription.TargetUrl, subscription.Events, subscription.IsActive);

    internal static WebhookDeliveryResponse ToDeliveryResponse(WebhookDelivery delivery) => new(
        delivery.Id, delivery.WebhookSubscriptionId, delivery.EventType, delivery.Attempt,
        delivery.ResponseStatus, delivery.ResponseSnippet, delivery.DeliveredAt, delivery.NextRetryAt);
}
