namespace Courier.Billing.Application;

public record PlanSummary(string PlanName, decimal MonthlyPriceNpr, Courier.Billing.Domain.SubscriptionStatus Status);
public record PlatformBillingMetrics(decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount);
public record InvoiceAdminRecord(Guid Id, Guid OrganizationId, string PlanName, decimal AmountNpr, Courier.Billing.Domain.InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);

public interface IBillingLookupService
{
    // Null means "no limit" (unlimited) OR "this org has no Subscription yet" — both cases the
    // caller (BranchService, Task 12) must treat identically as "do not block."
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
}
