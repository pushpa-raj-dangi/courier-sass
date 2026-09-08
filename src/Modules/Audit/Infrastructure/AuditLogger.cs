using Courier.Audit.Application;
using Courier.Audit.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Courier.Audit.Infrastructure;

public class AuditLogger : IAuditLogger
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
}
