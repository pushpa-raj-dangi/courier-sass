using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.SharedKernel;

namespace Courier.Platform.Application;

public record AuditLogRecordWithOrgName(
    Guid Id, Guid? OrganizationId, string? OrganizationName, string Action, string EntityType, string EntityId,
    Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction, DateTimeOffset Timestamp,
    string? Before, string? After, string? IpAddress, string? UserAgent);

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

        // Bounded per-page organization-name lookup, same "N+1 is fine once bounded by
        // pagination" reasoning as PlatformDashboardService.ListAllInvoicesAsync — capped at
        // pageSize (max 200) lookups, deduplicated within the page via nameById.
        var nameById = new Dictionary<Guid, string>();
        var withNames = new List<AuditLogRecordWithOrgName>();
        foreach (var record in result.Items)
        {
            string? organizationName = null;
            if (record.OrganizationId is Guid orgId)
            {
                if (!nameById.TryGetValue(orgId, out organizationName!))
                {
                    var org = await _organizationLookupService.GetAsync(orgId, cancellationToken);
                    organizationName = org?.Name ?? "Unknown";
                    nameById[orgId] = organizationName;
                }
            }

            withNames.Add(new AuditLogRecordWithOrgName(
                record.Id, record.OrganizationId, organizationName, record.Action, record.EntityType, record.EntityId,
                record.UserId, record.ImpersonatedByUserId, record.IsCrossTenantAction, record.Timestamp,
                record.Before, record.After, record.IpAddress, record.UserAgent));
        }

        return new PagedResult<AuditLogRecordWithOrgName>(withNames, result.Page, result.PageSize, result.TotalCount);
    }
}
