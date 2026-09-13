using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.SharedKernel;

namespace Courier.Platform.Application;

public record AuditLogRecordWithOrgName(
    Guid Id, Guid? OrganizationId, string? OrganizationName, string Action, string EntityType, string EntityId,
    string? EntityName, Guid? UserId, string? ActorName, Guid? ImpersonatedByUserId, bool IsCrossTenantAction,
    DateTimeOffset Timestamp, string? Before, string? After, string? IpAddress, string? UserAgent);

public class PlatformAuditService
{
    private readonly IAuditLogReader _auditLogReader;
    private readonly IOrganizationLookupService _organizationLookupService;

    public PlatformAuditService(IAuditLogReader auditLogReader, IOrganizationLookupService organizationLookupService)
    {
        _auditLogReader = auditLogReader;
        _organizationLookupService = organizationLookupService;
    }

    public async Task<PagedResult<AuditLogRecordWithOrgName>> SearchAsync(
        Guid? organizationId, string? organizationSearch, string? search, string? action, string? entityType,
        string? entityId, Guid? userId, DateTimeOffset? dateFrom, DateTimeOffset? dateTo, int? page, int? pageSize,
        CancellationToken cancellationToken = default)
    {
        // organizationId (an exact match, for a future direct link e.g. from the org detail
        // Sheet) takes precedence over organizationSearch (a name substring) — an exact link
        // should never be silently widened by a stale search box value.
        IReadOnlySet<Guid>? organizationIds = null;
        if (organizationId is Guid exactOrgId)
        {
            organizationIds = new HashSet<Guid> { exactOrgId };
        }
        else if (!string.IsNullOrWhiteSpace(organizationSearch))
        {
            // Bounded the same way PlatformDashboardService.ListAllInvoicesAsync's own org-name
            // search already is: a name search realistically matches a handful of orgs, not
            // hundreds, so capping at MaxPageSize matches is a documented limitation, not a bug.
            var matches = await _organizationLookupService.ListAsync(organizationSearch, null, null, 1, PaginationDefaults.MaxPageSize, cancellationToken);
            organizationIds = matches.Items.Select(o => o.Id).ToHashSet();
        }

        var filter = new AuditLogSearchFilter(search, action, entityType, entityId, userId, dateFrom, dateTo);
        var result = await _auditLogReader.SearchAcrossOrganizationsAsync(organizationIds, filter, page, pageSize, cancellationToken);

        // Bounded per-page organization/user-name lookups, same "N+1 is fine once bounded by
        // pagination" reasoning as PlatformDashboardService.ListAllInvoicesAsync — each capped at
        // pageSize (max 200) lookups, deduplicated within the page via the two *ById caches.
        var orgNameById = new Dictionary<Guid, string>();
        var userNameById = new Dictionary<Guid, string?>();

        async Task<string> ResolveOrgNameAsync(Guid orgId)
        {
            if (!orgNameById.TryGetValue(orgId, out var name))
            {
                var org = await _organizationLookupService.GetAsync(orgId, cancellationToken);
                name = org?.Name ?? "Unknown";
                orgNameById[orgId] = name;
            }
            return name;
        }

        async Task<string?> ResolveUserNameAsync(Guid userId)
        {
            if (!userNameById.TryGetValue(userId, out var name))
            {
                name = await _organizationLookupService.GetUserNameAsync(userId, cancellationToken);
                userNameById[userId] = name;
            }
            return name;
        }

        var withNames = new List<AuditLogRecordWithOrgName>();
        foreach (var record in result.Items)
        {
            var organizationName = record.OrganizationId is Guid orgId ? await ResolveOrgNameAsync(orgId) : null;
            var actorName = record.UserId is Guid actorId ? await ResolveUserNameAsync(actorId) : null;

            // Scoped deliberately to just these two entity types (the ones the Audit Logs UI's
            // Actor/Organization columns already resolve names for) rather than a generic
            // resolver across all ~30 audited entity types, which would need a lookup service per
            // module. Every other EntityType keeps showing "Type (id)", same as before this change.
            string? entityName = record.EntityType switch
            {
                "Organization" when Guid.TryParse(record.EntityId, out var entityOrgId) => await ResolveOrgNameAsync(entityOrgId),
                "User" when Guid.TryParse(record.EntityId, out var entityUserId) => await ResolveUserNameAsync(entityUserId),
                _ => null,
            };

            withNames.Add(new AuditLogRecordWithOrgName(
                record.Id, record.OrganizationId, organizationName, record.Action, record.EntityType, record.EntityId,
                entityName, record.UserId, actorName, record.ImpersonatedByUserId, record.IsCrossTenantAction,
                record.Timestamp, record.Before, record.After, record.IpAddress, record.UserAgent));
        }

        return new PagedResult<AuditLogRecordWithOrgName>(withNames, result.Page, result.PageSize, result.TotalCount);
    }
}
