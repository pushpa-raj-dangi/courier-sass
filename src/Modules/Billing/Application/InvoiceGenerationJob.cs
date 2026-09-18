using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Application;

// Hangfire recurring job (registered in Program.cs, Task 6). Runs with no ambient tenant
// context, so it reads across every organization directly via IgnoreQueryFilters() — the same
// legitimate cross-tenant background-job pattern Events.OutboxPublisherJob already established.
public class InvoiceGenerationJob
{
    private readonly DbContext _dbContext;
    private readonly IOptionsMonitor<BillingOptions> _optionsMonitor;

    public InvoiceGenerationJob(DbContext dbContext, IOptionsMonitor<BillingOptions> optionsMonitor)
    {
        _dbContext = dbContext;
        _optionsMonitor = optionsMonitor;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var dueSubscriptions = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => s.CurrentCycleEnd <= now && s.Status == SubscriptionStatus.Active)
            .ToListAsync(cancellationToken);

        if (dueSubscriptions.Count == 0)
        {
            return;
        }

        // PlanId is nullable on Subscription (Trial/TrialExpired carry no Plan), but this job only
        // ever looks at Status == Active subscriptions, and AssignPlanAsync is the only path that
        // sets Status to Active — it always sets PlanId in the same write, so PlanId is guaranteed
        // non-null for every subscription reached here.
        var planIds = dueSubscriptions.Select(s => s.PlanId!.Value).ToHashSet();
        var plans = await _dbContext.Set<Plan>().IgnoreQueryFilters()
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var subscription in dueSubscriptions)
        {
            var plan = plans[subscription.PlanId!.Value];
            var cycleStart = subscription.CurrentCycleStart;
            var cycleEnd = subscription.CurrentCycleEnd;

            // Zero-price (free/default) plans never generate an invoice — the org's cycle still
            // advances so a later plan upgrade starts counting from a clean cycle boundary.
            if (plan.MonthlyPriceNpr > 0)
            {
                var alreadyBilled = await _dbContext.Set<Invoice>().IgnoreQueryFilters()
                    .AnyAsync(i => i.SubscriptionId == subscription.Id && i.CycleStart == cycleStart, cancellationToken);
                if (!alreadyBilled)
                {
                    _dbContext.Set<Invoice>().Add(new Invoice
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = subscription.OrganizationId,
                        SubscriptionId = subscription.Id,
                        PlanId = plan.Id,
                        PlanNameSnapshot = plan.Name,
                        AmountNprSnapshot = plan.MonthlyPriceNpr,
                        CycleStart = cycleStart,
                        CycleEnd = cycleEnd,
                        Status = InvoiceStatus.Pending,
                        DueAt = cycleEnd.AddDays(_optionsMonitor.CurrentValue.GraceDays),
                        CreatedAt = now
                    });
                }
            }

            subscription.CurrentCycleStart = cycleEnd;
            subscription.CurrentCycleEnd = cycleEnd.AddMonths(1);
            subscription.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
