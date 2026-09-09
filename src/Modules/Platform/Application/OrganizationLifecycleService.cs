using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Merchants.Application;

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

    public async Task<List<OrganizationAdminSummary>> ListOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        var organizations = await _organizationLookupService.ListAsync(cancellationToken);
        var summaries = new List<OrganizationAdminSummary>();
        foreach (var organization in organizations)
        {
            var planSummary = await _billingLookupService.GetPlanSummaryAsync(organization.Id, cancellationToken);
            var branchCount = await _branchLookupService.CountActiveBranchesAsync(organization.Id, cancellationToken);
            summaries.Add(new OrganizationAdminSummary(organization.Id, organization.Name, organization.Slug, planSummary?.PlanName, organization.IsActive, organization.DeletedAt is not null, branchCount));
        }
        return summaries;
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
