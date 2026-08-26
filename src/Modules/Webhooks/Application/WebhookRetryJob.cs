using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Application;

// Hangfire recurring job (registered in Program.cs). WebhookDelivery has no own OrganizationId
// (Task 9) — no IgnoreQueryFilters() call needed here, unlike Events.Application.
// OutboxPublisherJob, since there is no tenant filter on this entity to bypass in the first
// place; "due for retry" is inherently a cross-tenant query by construction.
public class WebhookRetryJob
{
    private readonly DbContext _dbContext;
    private readonly IWebhookDeliveryAttemptService _attemptService;

    public WebhookRetryJob(DbContext dbContext, IWebhookDeliveryAttemptService attemptService)
    {
        _dbContext = dbContext;
        _attemptService = attemptService;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var dueDeliveryIds = await _dbContext.Set<WebhookDelivery>()
            .Where(d => d.NextRetryAt != null && d.NextRetryAt <= DateTimeOffset.UtcNow)
            .OrderBy(d => d.NextRetryAt)
            .Select(d => d.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var deliveryId in dueDeliveryIds)
        {
            await _attemptService.AttemptAsync(deliveryId, cancellationToken);
        }
    }
}
