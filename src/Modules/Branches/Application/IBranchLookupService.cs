using Courier.SharedKernel;

namespace Courier.Branches.Application;

public record BranchZoneInfo(Guid BranchId, Guid? DeliveryZoneId);
public record BranchAdminRecord(Guid Id, string Code, string Name, bool IsActive);

public interface IBranchLookupService
{
    // Null iff no branch with this id exists (or it's outside the caller's tenant, via the
    // existing query filter). A non-null result with DeliveryZoneId == null means the branch
    // exists but has no rating zone assigned yet.
    Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default);

    // Cross-tenant read for Platform's OrganizationLifecycleService (SuperAdmin org list) — always
    // called with ICurrentTenantProvider.OrganizationId == null, so the implementation must
    // IgnoreQueryFilters(); the explicit organizationId parameter in the Where clause is the real
    // scope, not the ambient tenant filter.
    Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Cross-tenant read for Platform's PlatformDashboardService (SuperAdmin org detail
    // drill-down, Branches section) — always called with ICurrentTenantProvider.OrganizationId
    // == null, so the implementation must IgnoreQueryFilters(); the explicit organizationId
    // parameter in the Where clause is the real, correct scope, not the ambient tenant filter.
    // Paginated — an org can have far more branches than fit in one Sheet.
    Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default);
}
