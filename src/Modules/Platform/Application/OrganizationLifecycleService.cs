using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;

namespace Courier.Platform.Application;

public record OrganizationAdminSummary(Guid Id, string Name, string Slug, string? PlanName, bool IsActive, bool IsDeleted, int BranchCount);

public class OrganizationLifecycleService
{
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly IMerchantSessionRevoker _merchantSessionRevoker;
    private readonly IBillingLookupService _billingLookupService;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IAuditLogger _auditLogger;

    public OrganizationLifecycleService(
        IOrganizationLookupService organizationLookupService,
        IMerchantSessionRevoker merchantSessionRevoker,
        IBillingLookupService billingLookupService,
        IBranchLookupService branchLookupService,
        IAuditLogger auditLogger)
    {
        _organizationLookupService = organizationLookupService;
        _merchantSessionRevoker = merchantSessionRevoker;
        _billingLookupService = billingLookupService;
        _branchLookupService = branchLookupService;
        _auditLogger = auditLogger;
    }

    // Paginated: the base org query (search/isActive/isDeleted/page/pageSize) is filtered and
    // paged FIRST by IOrganizationLookupService.ListAsync — the per-row plan/branch-count
    // enrichment loop below then only ever iterates the page's rows (bounded by pageSize, max
    // 200), not the whole organizations table. This is what fixes the N+1-across-every-org
    // performance gap: the loop itself is unchanged, only its input is now bounded.
    public async Task<PagedResult<OrganizationAdminSummary>> ListOrganizationsAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var organizations = await _organizationLookupService.ListAsync(search, isActive, isDeleted, page, pageSize, cancellationToken);
        var summaries = new List<OrganizationAdminSummary>();
        foreach (var organization in organizations.Items)
        {
            var planSummary = await _billingLookupService.GetPlanSummaryAsync(organization.Id, cancellationToken);
            var branchCount = await _branchLookupService.CountActiveBranchesAsync(organization.Id, cancellationToken);
            summaries.Add(new OrganizationAdminSummary(organization.Id, organization.Name, organization.Slug, planSummary?.PlanName, organization.IsActive, organization.DeletedAt is not null, branchCount));
        }
        return new PagedResult<OrganizationAdminSummary>(summaries, organizations.Page, organizations.PageSize, organizations.TotalCount);
    }

    public async Task SuspendAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.SuspendAsync(organizationId, cancellationToken);
        await _organizationLookupService.RevokeStaffRefreshTokensAsync(organizationId, cancellationToken);
        await _merchantSessionRevoker.RevokeAllForOrganizationAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.suspended", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }

    public async Task ReactivateAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.ReactivateAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.reactivated", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.SoftDeleteAsync(organizationId, cancellationToken);
        await _organizationLookupService.RevokeStaffRefreshTokensAsync(organizationId, cancellationToken);
        await _merchantSessionRevoker.RevokeAllForOrganizationAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.deleted", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }

    public async Task RestoreAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.RestoreAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.restored", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }
}
