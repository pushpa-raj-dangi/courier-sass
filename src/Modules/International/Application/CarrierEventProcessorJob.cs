using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

// Hangfire recurring job "carrier-event-processor" (every 30 s) and enqueued after every insert.
// Claims each Pending event atomically (Pending -> Processing), so concurrent runs — recurring,
// enqueued, or a test invoking RunAsync directly — never apply the same event twice. Each
// organization's events are applied inside that organization's tenant scope.
public class CarrierEventProcessorJob
{
    public const int BatchSize = 200;
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(5);

    private readonly DbContext _dbContext;
    private readonly ITenantScopeFactory _tenantScopeFactory;
    private readonly ILogger<CarrierEventProcessorJob> _logger;

    public CarrierEventProcessorJob(DbContext dbContext, ITenantScopeFactory tenantScopeFactory, ILogger<CarrierEventProcessorJob> logger)
    {
        _dbContext = dbContext;
        _tenantScopeFactory = tenantScopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Deliberate IgnoreQueryFilters (same as OutboxPublisherJob): this job runs without a
        // tenant and reads across tenants only to find work; all application of events happens in
        // per-organization tenant scopes below.
        var staleBefore = DateTimeOffset.UtcNow - StaleClaimAge;
        await _dbContext.Set<CarrierTrackingEvent>().IgnoreQueryFilters()
            .Where(e => e.ProcessingStatus == CarrierEventProcessingStatus.Processing && e.ClaimedAt < staleBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ProcessingStatus, CarrierEventProcessingStatus.Pending)
                .SetProperty(e => e.ClaimedAt, (DateTimeOffset?)null), cancellationToken);

        var candidates = await _dbContext.Set<CarrierTrackingEvent>().IgnoreQueryFilters()
            .Where(e => e.ProcessingStatus == CarrierEventProcessingStatus.Pending)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.ReceivedAt)
            .Take(BatchSize)
            .Select(e => new { e.Id, e.OrganizationId })
            .ToListAsync(cancellationToken);

        foreach (var organization in candidates.GroupBy(c => c.OrganizationId))
        {
            await using var scope = _tenantScopeFactory.CreateScope(organization.Key);
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var applier = scope.ServiceProvider.GetRequiredService<CarrierEventApplier>();
            foreach (var candidate in organization) // still OccurredAt order
            {
                var claimedAt = DateTimeOffset.UtcNow;
                var claimed = await dbContext.Set<CarrierTrackingEvent>()
                    .Where(e => e.Id == candidate.Id && e.ProcessingStatus == CarrierEventProcessingStatus.Pending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.ProcessingStatus, CarrierEventProcessingStatus.Processing)
                        .SetProperty(e => e.ClaimedAt, claimedAt), cancellationToken);
                if (claimed == 0)
                {
                    continue; // another run owns it
                }
                try
                {
                    await applier.ApplyAsync(candidate.Id, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Left in Processing; reclaimed after StaleClaimAge.
                    _logger.LogError(ex, "Applying carrier tracking event {CarrierTrackingEventId} failed.", candidate.Id);
                }
            }
        }
    }
}
