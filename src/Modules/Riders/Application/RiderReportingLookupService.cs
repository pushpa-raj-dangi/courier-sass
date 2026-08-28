using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public class RiderReportingLookupService : IRiderReportingLookupService
{
    private readonly DbContext _dbContext;

    public RiderReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RiderTaskLoad>> GetBranchRiderTaskLoadAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        // Rider is ambient-filtered; this list is therefore already tenant-scoped before the
        // DeliveryTask/DeliveryRun join below ever runs.
        var riderIds = await _dbContext.Set<Rider>().AsNoTracking()
            .Where(r => r.BaseBranchId == branchId && r.IsActive)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var openTaskCounts = await (
            from task in _dbContext.Set<DeliveryTask>().AsNoTracking()
            join run in _dbContext.Set<DeliveryRun>().AsNoTracking() on task.DeliveryRunId equals run.Id
            where task.Status == DeliveryTaskStatus.Pending && riderIds.Contains(run.RiderId)
            group task by run.RiderId into g
            select new { RiderId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countsByRider = openTaskCounts.ToDictionary(x => x.RiderId, x => x.Count);
        return riderIds.Select(riderId => new RiderTaskLoad(riderId, countsByRider.GetValueOrDefault(riderId))).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Method-syntax LINQ (not query-syntax) is deliberate here: a parameter named `from` used
        // inside a query-expression's `where`/`join` clauses collides with the `from` contextual
        // keyword and fails to compile (CS1525) — method syntax has no such ambiguity.
        var query = _dbContext.Set<DeliveryAttempt>().AsNoTracking()
            .Join(_dbContext.Set<Rider>().AsNoTracking(), attempt => attempt.RiderId, rider => rider.Id, (attempt, rider) => new { rider.BaseBranchId, attempt.Succeeded, attempt.AttemptedAt })
            .Where(x => x.AttemptedAt >= from && x.AttemptedAt < to);

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.BaseBranchId));
        }

        var grouped = await query
            .GroupBy(x => x.BaseBranchId)
            .Select(g => new { BranchId = g.Key, Attempts = g.Count(), Successes = g.Count(x => x.Succeeded) })
            .ToListAsync(cancellationToken);

        return grouped.ToDictionary(g => g.BranchId, g => new DeliverySuccessStats(g.Attempts, g.Successes));
    }

    public async Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByRiderAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<DeliveryAttempt>().AsNoTracking()
            .Join(_dbContext.Set<Rider>().AsNoTracking(), attempt => attempt.RiderId, rider => rider.Id, (attempt, rider) => new { RiderId = rider.Id, rider.BaseBranchId, attempt.Succeeded, attempt.AttemptedAt })
            .Where(x => x.AttemptedAt >= from && x.AttemptedAt < to);

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.BaseBranchId));
        }

        var grouped = await query
            .GroupBy(x => x.RiderId)
            .Select(g => new { RiderId = g.Key, Attempts = g.Count(), Successes = g.Count(x => x.Succeeded) })
            .ToListAsync(cancellationToken);

        return grouped.ToDictionary(g => g.RiderId, g => new DeliverySuccessStats(g.Attempts, g.Successes));
    }
}
