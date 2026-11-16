using System.Security.Claims;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    // F15 R3b-2 riders board: one row per rider based in the caller's rider.read scope (inactive
    // riders included, flagged), narrowed by branchId. A branchId outside scope narrows to nothing.
    // "Today" is the UTC date, the same as GetMyTasksTodayAsync and RecordLocationPingAsync.
    public async Task<RiderBoardResponse> GetBoardAsync(Guid? branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Rider>().AsNoTracking().AsQueryable();
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rider.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
            query = query.Where(r => accessibleBranchIdsArray.Contains(r.BaseBranchId));
        }
        if (branchId is Guid filterBranchId)
        {
            query = query.Where(r => r.BaseBranchId == filterBranchId);
        }

        var riders = await query.ToListAsync(cancellationToken);
        var rows = await BuildBoardRowsAsync(riders, cancellationToken);
        return new RiderBoardResponse(rows
            .OrderBy(BoardStageRank)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static int BoardStageRank(RiderBoardRow row) => row.Run?.Status switch
    {
        DeliveryRunStatus.Active => 0,
        DeliveryRunStatus.Completed => 1,
        _ => 2
    };

    // The domain should allow one run per rider per day; if there are more, the Active one wins,
    // then the highest Id, so the choice is deterministic.
    private static DeliveryRun PickTodayRun(IEnumerable<DeliveryRun> runs) =>
        runs.OrderBy(r => r.Status == DeliveryRunStatus.Active ? 0 : 1).ThenByDescending(r => r.Id).First();

    // A fixed number of queries whatever the rider count: runs, grouped task counts, latest ping,
    // vehicles, names. DeliveryRun/DeliveryTask/RiderLocationPing carry no OrganizationId; every
    // query here is keyed by rider ids that came from the tenant-filtered Rider set.
    private async Task<List<RiderBoardRow>> BuildBoardRowsAsync(IReadOnlyCollection<Rider> riders, CancellationToken cancellationToken)
    {
        if (riders.Count == 0)
        {
            return new List<RiderBoardRow>();
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var todayStart = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var riderIds = riders.Select(r => r.Id).ToArray();

        var runs = await _dbContext.Set<DeliveryRun>().AsNoTracking()
            .Where(r => riderIds.Contains(r.RiderId) && r.Date == today)
            .ToListAsync(cancellationToken);
        var runByRider = runs.GroupBy(r => r.RiderId).ToDictionary(g => g.Key, g => PickTodayRun(g));
        var runIds = runByRider.Values.Select(r => r.Id).ToArray();

        var taskCounts = await _dbContext.Set<DeliveryTask>().AsNoTracking()
            .Where(t => runIds.Contains(t.DeliveryRunId))
            .GroupBy(t => new { t.DeliveryRunId, t.Status })
            .Select(g => new { g.Key.DeliveryRunId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var lastPings = await _dbContext.Set<RiderLocationPing>().AsNoTracking()
            .Where(p => riderIds.Contains(p.RiderId) && p.ReceivedAt >= todayStart)
            .GroupBy(p => p.RiderId)
            .Select(g => new { RiderId = g.Key, LastPingAt = g.Max(p => p.ReceivedAt) })
            .ToListAsync(cancellationToken);
        var lastPingByRider = lastPings.ToDictionary(p => p.RiderId, p => p.LastPingAt);

        var vehicles = await _dbContext.Set<Vehicle>().AsNoTracking()
            .Where(v => v.RiderId != null && riderIds.Contains(v.RiderId.Value))
            .Select(v => new { RiderId = v.RiderId!.Value, v.PlateNumber })
            .ToListAsync(cancellationToken);
        var plateByRider = vehicles
            .GroupBy(v => v.RiderId)
            .ToDictionary(g => g.Key, g => g.Select(v => v.PlateNumber).OrderBy(p => p, StringComparer.Ordinal).First());

        var names = await _userDirectoryLookupService.GetFullNamesAsync(riders.Select(r => r.UserId).Distinct().ToArray(), cancellationToken);

        int CountTasks(Guid runId, DeliveryTaskStatus? status) =>
            taskCounts.Where(c => c.DeliveryRunId == runId && (status == null || c.Status == status)).Sum(c => c.Count);

        return riders.Select(rider =>
        {
            runByRider.TryGetValue(rider.Id, out var run);
            return new RiderBoardRow(
                rider.Id,
                rider.UserId,
                names.TryGetValue(rider.UserId, out var fullName) ? fullName : "Rider",
                rider.BaseBranchId,
                rider.RoleType,
                rider.IsActive,
                plateByRider.TryGetValue(rider.Id, out var plate) ? plate : null,
                run is null ? null : new RiderBoardRunSummary(run.Id, run.Status),
                run is null ? 0 : CountTasks(run.Id, null),
                run is null ? 0 : CountTasks(run.Id, DeliveryTaskStatus.Completed),
                run is null ? 0 : CountTasks(run.Id, DeliveryTaskStatus.Failed),
                lastPingByRider.TryGetValue(rider.Id, out var pingAt) ? pingAt : null);
        }).ToList();
    }
}
