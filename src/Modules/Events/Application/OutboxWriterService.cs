using System.Text.Json;
using Courier.Events.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Events.Application;

public class OutboxWriterService : IOutboxWriter
{
    private readonly DbContext _dbContext;

    public OutboxWriterService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Deliberately does NOT call SaveChangesAsync itself — the caller's own SaveChangesAsync
    // (already in flight for the business change that triggered this event) persists this row
    // too, since both operations share the same DbContext instance in the same request scope.
    // This is what makes the outbox write transactional with the business change for free,
    // without Events needing to know anything about transactions or the caller's own logic.
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            Attempts = 0
        });
        return Task.CompletedTask;
    }
}
