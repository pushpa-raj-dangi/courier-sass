using Courier.Audit.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Application;

public record SubscriptionResponse(Guid Id, Guid OrganizationId, Guid PlanId, string PlanName, SubscriptionStatus Status, DateTimeOffset CurrentCycleStart, DateTimeOffset CurrentCycleEnd);
public record InvoiceResponse(Guid Id, string PlanName, decimal AmountNpr, DateTimeOffset CycleStart, DateTimeOffset CycleEnd, InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);

public class SubscriptionService : IBillingLookupService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly BillingOptions _options;
    private readonly ICurrentTenantProvider _currentTenantProvider;

    public SubscriptionService(DbContext dbContext, IAuditLogger auditLogger, IOptions<BillingOptions> options, ICurrentTenantProvider currentTenantProvider)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _options = options.Value;
        _currentTenantProvider = currentTenantProvider;
    }

    // Called by the staff self-serve /billing/plan endpoint (real ambient tenant, Task 3) and by
    // Platform's SuperAdmin org-detail Sheet (null ambient tenant, Task 6) to upgrade/downgrade/
    // first-assign an org's Plan.
    public async Task<SubscriptionResponse> AssignPlanAsync(Guid organizationId, Guid planId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Set<Plan>().FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, cancellationToken)
            ?? throw new BillingValidationException("Plan was not found or is retired.");

        var existing = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
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
        else if (existing.CurrentCycleEnd <= now)
        {
            // A trial (or a previous paid cycle) that already elapsed by the time of conversion
            // must not be back-billed for the dormant window, and must not be flagged Overdue/
            // suspended the very next night by OverdueInvoiceJob for an invoice that was already
            // overdue the moment InvoiceGenerationJob created it. A mid-trial conversion (cycle
            // not yet elapsed) intentionally falls through and keeps its original cycle — see
            // spec §6.2.
            existing.CurrentCycleStart = now;
            existing.CurrentCycleEnd = now.AddMonths(1);
        }
        existing.PlanId = plan.Id;
        existing.Status = SubscriptionStatus.Active;
        existing.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // IsCrossTenantAction is derived from the real ambient tenant, never trusted from a
        // caller-supplied flag — true whenever the acting request's own org differs from (or has
        // no) the organization being changed, which is always true for a null-ambient-tenant
        // Platform caller and always false for an org's own self-serve call.
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "subscription.plan_assigned", "Subscription", existing.Id.ToString(), IsCrossTenantAction: _currentTenantProvider.OrganizationId != organizationId), cancellationToken);

        return new SubscriptionResponse(existing.Id, existing.OrganizationId, existing.PlanId!.Value, plan.Name, existing.Status, existing.CurrentCycleStart, existing.CurrentCycleEnd);
    }

    // Called exactly once per organization, from AuthService.RegisterOrganizationAsync via
    // IOrganizationSubscriptionStarter (Task 2). Deliberately assigns no Plan — a fresh platform
    // install with zero Plans configured yet must still let organizations register.
    public async Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        // Idempotent: a second call for the same org (a caller retry, e.g. Finding 4's fail-open
        // registration path combined with a higher-level retry) is a silent no-op rather than a
        // DbUpdateException from the unique index on Subscriptions.OrganizationId — makes the
        // "called once" contract in this method's doc comment above actually true.
        var alreadyExists = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .AnyAsync(s => s.OrganizationId == organizationId, cancellationToken);
        if (alreadyExists)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PlanId = null,
            Status = SubscriptionStatus.Trial,
            TrialEndsAt = now.AddDays(_options.TrialDays),
            CurrentCycleStart = now,
            CurrentCycleEnd = now.AddMonths(1),
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<Subscription>().Add(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "subscription.trial_started", "Subscription", subscription.Id.ToString()), cancellationToken);
    }

    public async Task<SubscriptionResponse?> GetCurrentAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var result = await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals (Guid?)p.Id
            where s.OrganizationId == organizationId
            select new SubscriptionResponse(s.Id, s.OrganizationId, p.Id, p.Name, s.Status, s.CurrentCycleStart, s.CurrentCycleEnd)
        ).FirstOrDefaultAsync(cancellationToken);
        return result;
    }

    public async Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals (Guid?)p.Id
            where s.OrganizationId == organizationId
            select (int?)p.MaxBranches
        ).FirstOrDefaultAsync(cancellationToken);
        // No Subscription row, or a Trial/TrialExpired row with no PlanId, both naturally yield
        // null here (the join simply finds no match) — treated as "unlimited," matching this
        // interface's existing fail-open default. A trial organization is not branch-limited.
    }

    // Dual-use: called under a REAL ambient tenant from BillingEndpoints (staff billing tab), and
    // under a NULL ambient tenant from Platform's OrganizationLifecycleService/PlatformDashboardService
    // (Platform always runs with no tenant context). IgnoreQueryFilters() is required so the
    // null-tenant caller isn't zeroed out by the fail-closed ambient filter — safe for the
    // real-tenant caller too, since the explicit `s.OrganizationId == organizationId` predicate
    // below already does the real scoping.
    public async Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>().IgnoreQueryFilters()
            join p in _dbContext.Set<Plan>().IgnoreQueryFilters() on s.PlanId equals (Guid?)p.Id
            where s.OrganizationId == organizationId
            select new PlanSummary(p.Name, p.MonthlyPriceNpr, s.Status)
        ).FirstOrDefaultAsync(cancellationToken);
    }

    // See IBillingLookupService.GetSubscriptionStatusAsync's doc comment — unlike
    // GetPlanSummaryAsync above, this returns a result even when PlanId is null (Trial/
    // TrialExpired with no Plan chosen yet). Same dual-use IgnoreQueryFilters() reasoning.
    public async Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        string? planName = null;
        decimal? monthlyPriceNpr = null;
        if (subscription.PlanId is Guid planId)
        {
            var plan = await _dbContext.Set<Plan>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
            planName = plan?.Name;
            monthlyPriceNpr = plan?.MonthlyPriceNpr;
        }

        return new SubscriptionStatusInfo(subscription.Status, subscription.TrialEndsAt, planName, monthlyPriceNpr);
    }

    // Consumed by TrialEnforcementMiddleware on every non-GET request — kept to a single-column
    // projection since it runs far more often than any other method here. The middleware always
    // supplies a real organizationId from the current request's own tenant claim, but this still
    // follows the same IgnoreQueryFilters()-plus-explicit-predicate convention as every other
    // method in this class, for consistency rather than correctness.
    public async Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var status = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId)
            .Select(s => (SubscriptionStatus?)s.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return status == SubscriptionStatus.TrialExpired;
    }

    // Dual-use, same reasoning as GetPlanSummaryAsync above — called under a real ambient tenant
    // (staff billing tab) and under a null ambient tenant (Platform org-detail Sheet's Invoices
    // section via ListInvoicesForOrganizationAsync below).
    public async Task<List<InvoiceResponse>> ListInvoicesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Invoice>().IgnoreQueryFilters()
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => i.CycleStart)
            .Select(i => new InvoiceResponse(i.Id, i.PlanNameSnapshot, i.AmountNprSnapshot, i.CycleStart, i.CycleEnd, i.Status, i.DueAt, i.PaidAt))
            .ToListAsync(cancellationToken);
    }

    // Cross-org by design (a platform-wide metric) — IgnoreQueryFilters() is required since
    // the caller (Platform) always runs with a null ambient tenant.
    public async Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default)
    {
        var mrr = await (
            from s in _dbContext.Set<Subscription>().IgnoreQueryFilters()
            join p in _dbContext.Set<Plan>().IgnoreQueryFilters() on s.PlanId equals (Guid?)p.Id
            where s.Status == SubscriptionStatus.Active
            select p.MonthlyPriceNpr
        ).SumAsync(cancellationToken);

        var overdueCount = await _dbContext.Set<Invoice>().IgnoreQueryFilters()
            .CountAsync(i => i.Status == InvoiceStatus.Overdue, cancellationToken);

        return new PlatformBillingMetrics(mrr, overdueCount);
    }

    public async Task<PagedResult<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Invoice>().IgnoreQueryFilters().Where(i => i.OrganizationId == organizationId);
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.CycleStart)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(i => new InvoiceResponse(i.Id, i.PlanNameSnapshot, i.AmountNprSnapshot, i.CycleStart, i.CycleEnd, i.Status, i.DueAt, i.PaidAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<InvoiceResponse>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    // Cross-org by design (Platform's SuperAdmin invoices list) — IgnoreQueryFilters() is
    // required since the caller always runs with a null ambient tenant, same as
    // GetPlatformMetricsAsync above.
    public async Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(InvoiceStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Invoice>().IgnoreQueryFilters().AsQueryable();
        if (status is InvoiceStatus s)
        {
            query = query.Where(i => i.Status == s);
        }
        return await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvoiceAdminRecord(i.Id, i.OrganizationId, i.PlanNameSnapshot, i.AmountNprSnapshot, i.Status, i.DueAt, i.PaidAt))
            .ToListAsync(cancellationToken);
    }
}
