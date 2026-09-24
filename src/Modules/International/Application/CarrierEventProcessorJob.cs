using System.Data.Common;
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
//
// The per-event claim alone is not enough: two overlapping runs can each claim a *different* event
// for the *same* shipment (e.g. run A claims an earlier TRANSIT, run B claims a later DELIVERED)
// and apply them out of order, or race on the same Shipment row and hit an xmin
// DbUpdateConcurrencyException. A Postgres session advisory lock serializes whole runs — recurring,
// enqueued, and a test's direct call all contend for the same lock, so only one run ever applies
// events at a time. The per-event atomic claim stays as defense in depth (e.g. if a run holding the
// lock is killed mid-batch, a later run's stale-claim sweep still reclaims its rows safely).
public class CarrierEventProcessorJob
{
    public const int BatchSize = 200;

    // Arbitrary but distinct bigint key for this job's advisory lock. Grepped the repo
    // (`pg_advisory_lock`/`pg_try_advisory_lock`) before picking it: this is the only place in the
    // codebase taking a session advisory lock, so there is no other key to collide with.
    private const long AdvisoryLockKey = 8_427_119_003;

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
        // The advisory lock is Postgres-only. Same IsRelational() guard as CodService.Settlements.cs
        // / AuthService.cs's transaction gating — against the in-memory provider (unit tests, which
        // never exercise this job directly per the plan) there is nothing to serialize against.
        if (!_dbContext.Database.IsRelational())
        {
            await ProcessBatchAsync(cancellationToken);
            return;
        }

        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await TryAcquireLockAsync(cancellationToken))
            {
                return; // another run is already processing; nothing to do here
            }
            try
            {
                await ProcessBatchAsync(cancellationToken);
            }
            finally
            {
                await ReleaseLockAsync(cancellationToken);
            }
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> TryAcquireLockAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateLockCommand("SELECT pg_try_advisory_lock(@key)");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task ReleaseLockAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateLockCommand("SELECT pg_advisory_unlock(@key)");
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private DbCommand CreateLockCommand(string commandText)
    {
        var command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = AdvisoryLockKey;
        command.Parameters.Add(parameter);
        return command;
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
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
