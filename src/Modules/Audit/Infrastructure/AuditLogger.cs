using Courier.Audit.Application;
using Courier.Audit.Domain;
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
            .Select(a => new AuditLogRecord(a.Id, a.Action, a.EntityType, a.EntityId, a.UserId, a.ImpersonatedByUserId, a.IsCrossTenantAction, a.Timestamp))
            .ToListAsync(cancellationToken);
    }
}
