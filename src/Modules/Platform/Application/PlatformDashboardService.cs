using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;

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

    // Joins invoices against organizations in-memory (a full org-name dictionary, then a
    // dictionary lookup per invoice) rather than an N+1 per-invoice organization lookup. Still
    // needs every organization's name (an invoice can belong to any org), so this loops
    // ListAsync's now-paginated pages to rebuild the full dictionary — a stopgap kept correct
    // (no org silently missing from the name lookup) pending the Invoices-area pagination pass,
    // which will redesign this join properly instead of paging through every organization here.
    public async Task<List<InvoiceAdminRecordWithOrgName>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default)
    {
        var invoices = await _billingLookupService.ListAllInvoicesAsync(status, cancellationToken);
        var namesByid = new Dictionary<Guid, string>();
        var page = 1;
        while (true)
        {
            var result = await _organizationLookupService.ListAsync(null, null, null, page, Courier.SharedKernel.PaginationDefaults.MaxPageSize, cancellationToken);
            foreach (var organization in result.Items)
            {
                namesByid[organization.Id] = organization.Name;
            }
            if (page >= result.TotalPages)
            {
                break;
            }
            page++;
        }

        return invoices
            .Select(i => new InvoiceAdminRecordWithOrgName(i.Id, i.OrganizationId, namesByid.GetValueOrDefault(i.OrganizationId, "Unknown"), i.PlanName, i.AmountNpr, i.Status, i.DueAt, i.PaidAt))
            .ToList();
    }
}
