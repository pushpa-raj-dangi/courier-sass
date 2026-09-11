using Courier.SharedKernel;

namespace Courier.Audit.Application;

public record AuditLogRecord(
    Guid Id, Guid? OrganizationId, string Action, string EntityType, string EntityId,
    Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction, DateTimeOffset Timestamp,
    string? Before, string? After, string? IpAddress, string? UserAgent);

public record AuditLogSearchFilter(
    string? Search, string? Action, string? EntityType, string? EntityId,
    Guid? UserId, DateTimeOffset? DateFrom, DateTimeOffset? DateTo);

public interface IAuditLogReader
{
    // Cross-tenant read for Platform's PlatformDashboardService (SuperAdmin org detail
    // drill-down) — always called with ICurrentTenantProvider.OrganizationId == null. The
    // implementation must IgnoreQueryFilters(); the explicit organizationId parameter in the
    // Where clause is the real, correct scope, not the ambient tenant filter.
    Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default);

    // Tenant-facing paginated/filterable read (Audit/Presentation's GET /api/v1/audit-logs).
    // organizationId is passed explicitly by the caller (the ambient tenant's own id) rather than
    // relying on AppDbContext's HasQueryFilter — same belt-and-suspenders reasoning as
    // ListForOrganizationAsync above.
    Task<PagedResult<AuditLogRecord>> SearchAsync(Guid? organizationId, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default);

    // Platform-facing cross-tenant read. organizationIds == null means no organization
    // restriction (every org); a non-null set (including an empty one) restricts to exactly
    // those organizations — used when Platform's caller narrowed by an org-name search.
    Task<PagedResult<AuditLogRecord>> SearchAcrossOrganizationsAsync(IReadOnlySet<Guid>? organizationIds, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default);
}
