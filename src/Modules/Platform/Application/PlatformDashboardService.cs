using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.SharedKernel;

namespace Courier.Platform.Application;

public record PlatformMetrics(int TotalOrganizations, int ActiveOrganizations, int SuspendedOrganizations, int DeletedOrganizations, decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount);
public record OrganizationDetail(OrganizationRecord Organization, string? PlanName, Courier.Billing.Domain.SubscriptionStatus? SubscriptionStatus, DateTimeOffset? TrialEndsAt, List<OrganizationUserRecord> Users, List<BranchAdminRecord> Branches, List<InvoiceResponse> Invoices, List<AuditLogRecord> RecentAuditEntries);
public record InvoiceAdminRecordWithOrgName(Guid Id, Guid OrganizationId, string OrganizationName, string PlanName, decimal AmountNpr, Courier.Billing.Domain.InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);

public class PlatformDashboardService
{
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly IBillingLookupService _billingLookupService;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IAuditLogReader _auditLogReader;

    public PlatformDashboardService(
        IOrganizationLookupService organizationLookupService,
        IBillingLookupService billingLookupService,
        IBranchLookupService branchLookupService,
        IAuditLogReader auditLogReader)
    {
        _organizationLookupService = organizationLookupService;
        _billingLookupService = billingLookupService;
        _branchLookupService = branchLookupService;
        _auditLogReader = auditLogReader;
    }

    public async Task<PlatformMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        // ListAsync is now paginated (organizations list can run to tens of thousands of rows) —
        // these dashboard counts only need PagedResult.TotalCount from each filtered query, not
        // the actual row data, so pageSize:1 keeps each call to a cheap COUNT plus one discarded
        // row rather than materializing the whole table 4 times.
        var totalCount = (await _organizationLookupService.ListAsync(null, null, null, 1, 1, cancellationToken)).TotalCount;
        var activeCount = (await _organizationLookupService.ListAsync(null, true, false, 1, 1, cancellationToken)).TotalCount;
        var suspendedCount = (await _organizationLookupService.ListAsync(null, false, false, 1, 1, cancellationToken)).TotalCount;
        var deletedCount = (await _organizationLookupService.ListAsync(null, null, true, 1, 1, cancellationToken)).TotalCount;
        var billingMetrics = await _billingLookupService.GetPlatformMetricsAsync(cancellationToken);

        return new PlatformMetrics(
            TotalOrganizations: totalCount,
            ActiveOrganizations: activeCount,
            SuspendedOrganizations: suspendedCount,
            DeletedOrganizations: deletedCount,
            MonthlyRecurringRevenueNpr: billingMetrics.MonthlyRecurringRevenueNpr,
            OverdueInvoiceCount: billingMetrics.OverdueInvoiceCount);
    }

    public async Task<OrganizationDetail?> GetOrganizationDetailAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _organizationLookupService.GetAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        var subscriptionStatus = await _billingLookupService.GetSubscriptionStatusAsync(organizationId, cancellationToken);
        var users = await _organizationLookupService.ListUsersAsync(organizationId, cancellationToken);
        var branches = await _branchLookupService.ListForOrganizationAsync(organizationId, cancellationToken);
        var invoices = await _billingLookupService.ListInvoicesForOrganizationAsync(organizationId, cancellationToken);
        var auditEntries = await _auditLogReader.ListForOrganizationAsync(organizationId, 50, cancellationToken);

        return new OrganizationDetail(organization, subscriptionStatus?.PlanName, subscriptionStatus?.Status, subscriptionStatus?.TrialEndsAt, users, branches, invoices, auditEntries);
    }

    // Bulk-fetches all invoices matching status (Billing's ListAllInvoicesAsync is a single flat
    // query — Invoices is a much smaller, slower-growing table than Organizations, so this
    // remains unbounded at the SQL level, unlike the paginated Organizations query below). A
    // search term is resolved to matching organization IDs first (capped at MaxPageSize matches
    // — a search term realistically matches a handful of orgs, not hundreds; a documented
    // limitation, not a silent bug) and used to filter invoices before pagination. Only the
    // resulting page's invoices then get an org-name lookup — bounded to pageSize (max 200)
    // per-invoice GetAsync calls, the same "N+1 is fine once bounded by pagination" reasoning as
    // OrganizationLifecycleService's plan/branch-count enrichment loop.
    public async Task<PagedResult<InvoiceAdminRecordWithOrgName>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, string? search, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var invoices = await _billingLookupService.ListAllInvoicesAsync(status, cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var matchingOrgs = await _organizationLookupService.ListAsync(search, null, null, 1, Courier.SharedKernel.PaginationDefaults.MaxPageSize, cancellationToken);
            var matchingIds = matchingOrgs.Items.Select(o => o.Id).ToHashSet();
            invoices = invoices.Where(i => matchingIds.Contains(i.OrganizationId)).ToList();
        }

        var (normalizedPage, normalizedPageSize) = Courier.SharedKernel.PaginationDefaults.Normalize(page, pageSize);
        var totalCount = invoices.Count;
        var pageItems = invoices.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList();

        var withNames = new List<InvoiceAdminRecordWithOrgName>();
        foreach (var invoice in pageItems)
        {
            var org = await _organizationLookupService.GetAsync(invoice.OrganizationId, cancellationToken);
            withNames.Add(new InvoiceAdminRecordWithOrgName(invoice.Id, invoice.OrganizationId, org?.Name ?? "Unknown", invoice.PlanName, invoice.AmountNpr, invoice.Status, invoice.DueAt, invoice.PaidAt));
        }

        return new PagedResult<InvoiceAdminRecordWithOrgName>(withNames, normalizedPage, normalizedPageSize, totalCount);
    }
}
