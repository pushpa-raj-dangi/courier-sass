using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

// Hangfire recurring job, same shape as Billing's TrialExpiryJob (no ambient tenant). Unlike that
// job, RiderLocationPing has no query filter to bypass at all (Task 1's design note), so no
// IgnoreQueryFilters() call is needed here — this simply deletes globally by age across every org.
public class RiderLocationPingRetentionJob
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(30);

    private readonly DbContext _dbContext;

    public RiderLocationPingRetentionJob(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - RetentionWindow;
        var expiredPings = await _dbContext.Set<RiderLocationPing>()
            .Where(p => p.RecordedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (expiredPings.Count == 0)
        {
            return;
        }

        _dbContext.Set<RiderLocationPing>().RemoveRange(expiredPings);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
