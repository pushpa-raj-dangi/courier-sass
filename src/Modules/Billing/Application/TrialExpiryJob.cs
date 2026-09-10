using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

// Hangfire recurring job, same "no ambient tenant, IgnoreQueryFilters() everywhere" shape as
// InvoiceGenerationJob/OverdueInvoiceJob. Unlike those two, this job needs no cross-module call —
// flipping Trial -> TrialExpired touches only this module's own Subscription table, so there is
// no equivalent of OverdueInvoiceJob's separate post-SaveChangesAsync organization-suspend step.
public class TrialExpiryJob
{
    private readonly DbContext _dbContext;

    public TrialExpiryJob(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredTrials = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Trial && s.TrialEndsAt != null && s.TrialEndsAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredTrials.Count == 0)
        {
            return;
        }

        foreach (var subscription in expiredTrials)
        {
            subscription.Status = SubscriptionStatus.TrialExpired;
            subscription.UpdatedAt = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
