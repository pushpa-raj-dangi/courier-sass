using Courier.Billing.Domain;
using Courier.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

// Hangfire recurring job, same "no ambient tenant, IgnoreQueryFilters() everywhere" shape as
// InvoiceGenerationJob (Task 3). DueAt already bakes in the grace period (Task 3's
// BillingOptions.GraceDays), so "DueAt < now" alone is the complete overdue condition — no
// second grace window layered on top here.
public class OverdueInvoiceJob
{
    private readonly DbContext _dbContext;
    private readonly IOrganizationLookupService _organizationLookupService;

    public OverdueInvoiceJob(DbContext dbContext, IOrganizationLookupService organizationLookupService)
    {
        _dbContext = dbContext;
        _organizationLookupService = organizationLookupService;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var overdueInvoices = await _dbContext.Set<Invoice>().IgnoreQueryFilters()
            .Where(i => i.Status == InvoiceStatus.Pending && i.DueAt < now)
            .ToListAsync(cancellationToken);

        if (overdueInvoices.Count == 0)
        {
            return;
        }

        var subscriptionIds = overdueInvoices.Select(i => i.SubscriptionId).ToHashSet();
        var subscriptions = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var organizationIdsToSuspend = new HashSet<Guid>();
        foreach (var invoice in overdueInvoices)
        {
            invoice.Status = InvoiceStatus.Overdue;
            if (subscriptions.TryGetValue(invoice.SubscriptionId, out var subscription) && subscription.Status != SubscriptionStatus.PastDue)
            {
                subscription.Status = SubscriptionStatus.PastDue;
                subscription.UpdatedAt = now;
            }
            organizationIdsToSuspend.Add(invoice.OrganizationId);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Outside the SaveChangesAsync above deliberately, same "cross-module call is not part
        // of this module's own atomic unit of work" reasoning as PaymentService.CompletePaymentAsync
        // (Task 6) — a crash here leaves invoices correctly Overdue but an org not yet suspended
        // until the job's next run picks it up again (it will: the invoice is still Overdue).
        foreach (var organizationId in organizationIdsToSuspend)
        {
            await _organizationLookupService.SuspendForNonPaymentAsync(organizationId, cancellationToken);
        }
    }
}
