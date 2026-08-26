using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Application;

public partial class WebhookService
{
    // Resets the attempt counter and re-sends immediately (API-SPEC.md §5) — a fresh attempt
    // cycle, not a continuation of the old one, so a delivery that failed all 5 attempts can be
    // replayed after the merchant fixes their endpoint rather than staying permanently terminal.
    public async Task<WebhookDeliveryResponse?> ReplayAsync(Guid organizationId, Guid merchantId, Guid deliveryId, IWebhookDeliveryAttemptService attemptService, CancellationToken cancellationToken = default)
    {
        var delivery = await _dbContext.Set<WebhookDelivery>()
            .Join(_dbContext.Set<WebhookSubscription>(), d => d.WebhookSubscriptionId, s => s.Id, (d, s) => new { Delivery = d, Subscription = s })
            .Where(x => x.Delivery.Id == deliveryId && x.Subscription.OrganizationId == organizationId && x.Subscription.MerchantId == merchantId)
            .Select(x => x.Delivery)
            .FirstOrDefaultAsync(cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        delivery.Attempt = 0;
        delivery.DeliveredAt = null;
        delivery.NextRetryAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await attemptService.AttemptAsync(delivery.Id, cancellationToken);
        await _dbContext.Entry(delivery).ReloadAsync(cancellationToken);
        return ToDeliveryResponse(delivery);
    }
}
