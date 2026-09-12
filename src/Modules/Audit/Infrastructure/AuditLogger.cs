using Courier.Audit.Application;
using Courier.Audit.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Courier.Audit.Infrastructure;

public class AuditLogger : IAuditLogger, IAuditLogReader
{
    private readonly DbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogger(DbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        // Null in every context with no ambient HTTP request (a Hangfire job, or a unit test
        // that never set up an HttpContext) — impersonation only ever happens over a real
        // request, so null is the correct, expected value there.
        var impersonatedByClaim = _httpContextAccessor.HttpContext?.User.FindFirst("impersonated_by")?.Value;
        var impersonatedByUserId = Guid.TryParse(impersonatedByClaim, out var parsed) ? parsed : (Guid?)null;

        _dbContext.Set<AuditLog>().Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = entry.OrganizationId,
            UserId = entry.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Before = entry.Before,
            After = entry.After,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            Timestamp = DateTimeOffset.UtcNow,
            IsCrossTenantAction = entry.IsCrossTenantAction,
            ImpersonatedByUserId = impersonatedByUserId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // IgnoreQueryFilters() here is deliberate and safe for the same reason as every other
    // Platform-facing cross-org read in this plan — Platform's ambient tenant is always null, and
    // the explicit organizationId Where clause is the real, correct scope, not the bypassed
    // ambient filter.
    public async Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<AuditLog>().IgnoreQueryFilters()
            .Where(a => a.OrganizationId == organizationId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AuditLogRecord(a.Id, a.OrganizationId, a.Action, a.EntityType, a.EntityId, a.UserId, a.ImpersonatedByUserId, a.IsCrossTenantAction, a.Timestamp, a.Before, a.After, a.IpAddress, a.UserAgent))
            .ToListAsync(cancellationToken);
    }

    public Task<PagedResult<AuditLogRecord>> SearchAsync(Guid? organizationId, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<AuditLog>().IgnoreQueryFilters().AsQueryable();
        if (organizationId is Guid orgId)
        {
            query = query.Where(a => a.OrganizationId == orgId);
        }
        return PageAsync(ApplyFilters(query, filter), page, pageSize, cancellationToken);
    }

    public Task<PagedResult<AuditLogRecord>> SearchAcrossOrganizationsAsync(IReadOnlySet<Guid>? organizationIds, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<AuditLog>().IgnoreQueryFilters().AsQueryable();
        if (organizationIds is not null)
        {
            query = query.Where(a => a.OrganizationId != null && organizationIds.Contains(a.OrganizationId.Value));
        }
        return PageAsync(ApplyFilters(query, filter), page, pageSize, cancellationToken);
    }

    private static IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> query, AuditLogSearchFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Case-insensitive via ToLower() on both sides, matching
            // OrganizationAdminService.ListAsync's existing search — this module has no
            // reference to Npgsql.EntityFrameworkCore.PostgreSQL (only provider-agnostic EF
            // Core), so EF.Functions.ILike isn't available here either.
            var term = filter.Search.ToLower();
            query = query.Where(a => a.Action.ToLower().Contains(term) || a.EntityType.ToLower().Contains(term) || a.EntityId.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(a => a.Action == filter.Action);
        }
        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            var entityType = filter.EntityType.ToLower();
            query = query.Where(a => a.EntityType.ToLower() == entityType);
        }
        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(a => a.EntityId == filter.EntityId);
        }
        if (filter.UserId is Guid userId)
        {
            query = query.Where(a => a.UserId == userId);
        }
        if (filter.DateFrom is DateTimeOffset dateFrom)
        {
            query = query.Where(a => a.Timestamp >= dateFrom);
        }
        if (filter.DateTo is DateTimeOffset dateTo)
        {
            query = query.Where(a => a.Timestamp <= dateTo);
        }
        return query;
    }

    private static async Task<PagedResult<AuditLogRecord>> PageAsync(IQueryable<AuditLog> query, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            // Inlined rather than a shared helper method: EF Core cannot translate a call to an
            // external method inside Select() to SQL against the real Postgres provider (only
            // against the InMemory provider these unit tests run on), so the same constructor
            // call as ListForOrganizationAsync above is repeated here rather than factored out.
            .Select(a => new AuditLogRecord(a.Id, a.OrganizationId, a.Action, a.EntityType, a.EntityId, a.UserId, a.ImpersonatedByUserId, a.IsCrossTenantAction, a.Timestamp, a.Before, a.After, a.IpAddress, a.UserAgent))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditLogRecord>(items, normalizedPage, normalizedPageSize, totalCount);
    }
}
