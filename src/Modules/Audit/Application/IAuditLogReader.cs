namespace Courier.Audit.Application;

public record AuditLogRecord(Guid Id, string Action, string EntityType, string EntityId, Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction, DateTimeOffset Timestamp);

public interface IAuditLogReader
{
    // Cross-tenant read for Platform's PlatformDashboardService (SuperAdmin org detail
    // drill-down) — always called with ICurrentTenantProvider.OrganizationId == null. The
    // implementation must IgnoreQueryFilters(); the explicit organizationId parameter in the
    // Where clause is the real, correct scope, not the ambient tenant filter.
    Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default);
}
