namespace Courier.Billing.Application;

public record PlanSummary(string PlanName, decimal MonthlyPriceNpr, Courier.Billing.Domain.SubscriptionStatus Status);
public record PlatformBillingMetrics(decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount);
public record InvoiceAdminRecord(Guid Id, Guid OrganizationId, string PlanName, decimal AmountNpr, Courier.Billing.Domain.InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);
// Unlike PlanSummary (which requires a chosen Plan), this reflects a Subscription's real state
// even during/after a trial, when PlanId is null — PlanName/MonthlyPriceNpr are null in that case.
public record SubscriptionStatusInfo(Courier.Billing.Domain.SubscriptionStatus Status, DateTimeOffset? TrialEndsAt, string? PlanName, decimal? MonthlyPriceNpr);

public interface IBillingLookupService
{
    // Null means "no limit" (unlimited) OR "this org has no Subscription yet" — both cases the
    // caller (BranchService, via IBranchPlanLimitProvider) must treat identically as "do not block."
    Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Cross-tenant read for Platform's PlatformDashboardService (SuperAdmin dashboard) — always
    // called with ICurrentTenantProvider.OrganizationId == null, so the implementation must
    // IgnoreQueryFilters().
    Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default);

    // Thin wrapper over SubscriptionService.ListInvoicesAsync, added to the interface so Platform
    // never needs to inject SubscriptionService directly (mirrors the rest of this interface's
    // narrow-lookup shape).
    Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Cross-org read for Platform's PlatformDashboardService (SuperAdmin invoices list) — like
    // GetPlatformMetricsAsync above, always called with a null ambient tenant, so the
    // implementation must IgnoreQueryFilters().
    Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default);

    // Starts a 7-day (BillingOptions.TrialDays) trial with no committed Plan — called once, from
    // Identity's AuthService.RegisterOrganizationAsync via the Api-layer
    // IOrganizationSubscriptionStarter adapter (Task 2), and never again for that organization.
    Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default);

    // True only when Status == TrialExpired — consumed by TrialEnforcementMiddleware (Task 5) on
    // every non-GET request. An org with no Subscription at all, or Active/PastDue/Trial/
    // Cancelled, is never blocked by this — only a lapsed, never-converted trial is.
    Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Unlike GetPlanSummaryAsync (which returns null whenever PlanId is null), this always
    // returns the Subscription's real state when one exists — used by the staff /billing page's
    // trial countdown and Platform's org-detail Sheet (Task 6/7/8).
    Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
