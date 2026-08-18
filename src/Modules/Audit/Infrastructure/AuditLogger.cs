using Courier.Audit.Application;
using Courier.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Audit.Infrastructure;

public class AuditLogger : IAuditLogger
{
    private readonly DbContext _dbContext;

    public AuditLogger(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
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
            IsCrossTenantAction = entry.IsCrossTenantAction
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
