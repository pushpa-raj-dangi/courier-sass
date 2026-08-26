using Courier.Events.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Events.Application;

// Hangfire recurring job (registered in Program.cs, Task 4 Step 4) — polls for unpublished
// OutboxMessage rows, publishes each to the shared topic exchange with the event type as
// routing key, marks PublishedAt. Runs with no ICurrentTenantProvider context (it's a
// background job, not a request), so it reads across every tenant directly via
// IgnoreQueryFilters() — this is the one legitimate cross-tenant read in this codebase,
// exactly analogous to how AuthService.RegisterOrganizationAsync bypasses the filter for its
// own pre-authentication uniqueness check.
public class OutboxPublisherJob
{
    private readonly DbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;

    public OutboxPublisherJob(DbContext dbContext, IEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var unpublished = await _dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var message in unpublished)
        {
            message.Attempts += 1;
            // Envelope, not the bare payload — EventId (this row's own Id) and OrganizationId
            // are separate columns on OutboxMessage, not part of the payload each producing
            // module built (Task 2). Both consumers (Notifications Task 7, Webhooks Task 11)
            // need OrganizationId to scope their work; Webhooks additionally needs EventId as
            // its delivery-row idempotency key.
            var envelope = System.Text.Json.JsonSerializer.Serialize(new { EventId = message.Id, message.OrganizationId, EventType = message.EventType, Data = System.Text.Json.JsonDocument.Parse(message.Payload).RootElement });
            await _eventPublisher.PublishAsync(RabbitMqTopology.DomainEventsExchange, message.EventType, envelope, cancellationToken);
            message.PublishedAt = DateTimeOffset.UtcNow;
        }

        if (unpublished.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
