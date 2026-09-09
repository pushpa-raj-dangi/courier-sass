using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;

namespace Courier.Platform.Application;

public record PlatformMetrics(int TotalOrganizations, int ActiveOrganizations, int SuspendedOrganizations, int DeletedOrganizations, decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount);
public record OrganizationDetail(OrganizationRecord Organization, string? PlanName, List<OrganizationUserRecord> Users, List<BranchAdminRecord> Branches, List<InvoiceResponse> Invoices, List<AuditLogRecord> RecentAuditEntries);
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
        var organizations = await _organizationLookupService.ListAsync(cancellationToken);
        var billingMetrics = await _billingLookupService.GetPlatformMetricsAsync(cancellationToken);

        return new PlatformMetrics(
            TotalOrganizations: organizations.Count,
            ActiveOrganizations: organizations.Count(o => o.IsActive && o.DeletedAt is null),
            SuspendedOrganizations: organizations.Count(o => !o.IsActive && o.DeletedAt is null),
            DeletedOrganizations: organizations.Count(o => o.DeletedAt is not null),
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

        var planSummary = await _billingLookupService.GetPlanSummaryAsync(organizationId, cancellationToken);
        var users = await _organizationLookupService.ListUsersAsync(organizationId, cancellationToken);
        var branches = await _branchLookupService.ListForOrganizationAsync(organizationId, cancellationToken);
        var invoices = await _billingLookupService.ListInvoicesForOrganizationAsync(organizationId, cancellationToken);
        var auditEntries = await _auditLogReader.ListForOrganizationAsync(organizationId, 50, cancellationToken);

        return new OrganizationDetail(organization, planSummary?.PlanName, users, branches, invoices, auditEntries);
    }

    // Joins invoices against organizations in-memory (one ListAsync call, then a dictionary
    // lookup per invoice) rather than an N+1 per-invoice organization lookup.
    public async Task<List<InvoiceAdminRecordWithOrgName>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default)
    {
        var invoices = await _billingLookupService.ListAllInvoicesAsync(status, cancellationToken);
        var organizations = await _organizationLookupService.ListAsync(cancellationToken);
        var namesByid = organizations.ToDictionary(o => o.Id, o => o.Name);

        return invoices
            .Select(i => new InvoiceAdminRecordWithOrgName(i.Id, i.OrganizationId, namesByid.GetValueOrDefault(i.OrganizationId, "Unknown"), i.PlanName, i.AmountNpr, i.Status, i.DueAt, i.PaidAt))
            .ToList();
    }
}
