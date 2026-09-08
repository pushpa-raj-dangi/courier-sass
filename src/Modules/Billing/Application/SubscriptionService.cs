using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

public record SubscriptionResponse(Guid Id, Guid OrganizationId, Guid PlanId, string PlanName, SubscriptionStatus Status, DateTimeOffset CurrentCycleStart, DateTimeOffset CurrentCycleEnd);
public record InvoiceResponse(Guid Id, string PlanName, decimal AmountNpr, DateTimeOffset CycleStart, DateTimeOffset CycleEnd, InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);

public class SubscriptionService : IBillingLookupService
{
    private readonly DbContext _dbContext;

    public SubscriptionService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Called once at org self-registration (Task 7) with the platform's IsDefault plan, and by
    // SuperAdmin (Task 9's PlatformEndpoints) to upgrade/downgrade an org onto a different Plan.
    public async Task<SubscriptionResponse> AssignPlanAsync(Guid organizationId, Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Set<Plan>().FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, cancellationToken)
            ?? throw new BillingValidationException("Plan was not found or is retired.");

        var existing = await _dbContext.Set<Subscription>().FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new Subscription
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CurrentCycleStart = now,
                CurrentCycleEnd = now.AddMonths(1),
                CreatedAt = now
            };
            _dbContext.Set<Subscription>().Add(existing);
        }
        existing.PlanId = plan.Id;
        existing.Status = SubscriptionStatus.Active;
        existing.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SubscriptionResponse(existing.Id, existing.OrganizationId, existing.PlanId, plan.Name, existing.Status, existing.CurrentCycleStart, existing.CurrentCycleEnd);
    }

    public async Task<SubscriptionResponse?> GetCurrentAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var result = await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals p.Id
            where s.OrganizationId == organizationId
            select new SubscriptionResponse(s.Id, s.OrganizationId, s.PlanId, p.Name, s.Status, s.CurrentCycleStart, s.CurrentCycleEnd)
        ).FirstOrDefaultAsync(cancellationToken);
        return result;
    }

    public async Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals p.Id
            where s.OrganizationId == organizationId
            select (int?)p.MaxBranches
        ).FirstOrDefaultAsync(cancellationToken);
        // No Subscription row at all (shouldn't happen post-Task-7, every org gets one at
        // registration) also naturally yields null here via FirstOrDefaultAsync on an int? —
        // treated as "unlimited," matching Task 1's fail-open default.
    }

    public async Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals p.Id
            where s.OrganizationId == organizationId
            select new PlanSummary(p.Name, p.MonthlyPriceNpr, s.Status)
        ).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<InvoiceResponse>> ListInvoicesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Invoice>()
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => i.CycleStart)
            .Select(i => new InvoiceResponse(i.Id, i.PlanNameSnapshot, i.AmountNprSnapshot, i.CycleStart, i.CycleEnd, i.Status, i.DueAt, i.PaidAt))
            .ToListAsync(cancellationToken);
    }
}
