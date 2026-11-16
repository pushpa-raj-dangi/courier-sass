using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Courier.Shipments.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

// F15 R4 rider PWA "my day". Self-scoped like GetMyTasksTodayAsync: the rider is resolved from the
// caller's user id, never from a request value, so a rider can only ever read their own day.
// A fixed number of queries: rider, runs, tasks, one batched task-detail read, held cash, branch
// name, vehicle plate. DeliveryRun/DeliveryTask carry no OrganizationId; both are reached only
// through the tenant-filtered Rider row.
public class RiderTodayService
{
    private readonly DbContext _dbContext;
    private readonly IShipmentTaskBatchLookupService _taskLookup;
    private readonly ICodRiderCashLookupService _cashLookup;
    private readonly IBranchLookupService _branchLookup;

    public RiderTodayService(
        DbContext dbContext,
        IShipmentTaskBatchLookupService taskLookup,
        ICodRiderCashLookupService cashLookup,
        IBranchLookupService branchLookup)
    {
        _dbContext = dbContext;
        _taskLookup = taskLookup;
        _cashLookup = cashLookup;
        _branchLookup = branchLookup;
    }

    public async Task<MyTodayResponse?> GetMyTodayAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var userId = actingUser.GetUserId();
        if (userId is null)
        {
            return null;
        }
        var rider = await _dbContext.Set<Rider>().AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var runs = await _dbContext.Set<DeliveryRun>().AsNoTracking()
            .Where(r => r.RiderId == rider.Id && r.Date == today)
            .ToListAsync(cancellationToken);
        var run = runs.Count == 0 ? null : RiderService.PickTodayRun(runs);

        var tasks = new List<MyTodayTask>();
        if (run is not null)
        {
            var runTasks = await _dbContext.Set<DeliveryTask>().AsNoTracking()
                .Where(t => t.DeliveryRunId == run.Id)
                .OrderBy(t => t.Sequence)
                .ToListAsync(cancellationToken);
            if (runTasks.Count > 0)
            {
                var infos = await _taskLookup.GetTaskInfosAsync(runTasks.Select(t => t.ShipmentId).ToHashSet(), cancellationToken);
                foreach (var task in runTasks)
                {
                    // Same rule as GetMyTasksTodayAsync: a task whose shipment can't be read is skipped.
                    if (!infos.TryGetValue(task.ShipmentId, out var info))
                    {
                        continue;
                    }
                    tasks.Add(new MyTodayTask(
                        task.Id, task.ShipmentId, info.Awb, task.Type, task.Sequence, task.Status,
                        info.RecipientName, info.RecipientPhone, info.RecipientAddressLine, info.RecipientWardNumber, info.CodAmount));
                }
            }
        }

        var counts = new MyTodayCounts(
            tasks.Count,
            tasks.Count(t => t.Status == DeliveryTaskStatus.Completed),
            tasks.Count(t => t.Status == DeliveryTaskStatus.Failed));

        var held = await _cashLookup.GetHeldByRiderAsync(rider.Id, cancellationToken);
        var branchNames = await _branchLookup.GetNamesByIdsAsync(new HashSet<Guid> { rider.BaseBranchId }, cancellationToken);
        var plates = await _dbContext.Set<Vehicle>().AsNoTracking()
            .Where(v => v.RiderId == rider.Id)
            .Select(v => v.PlateNumber)
            .ToListAsync(cancellationToken);

        return new MyTodayResponse(
            run is null ? null : new RiderBoardRunSummary(run.Id, run.Status),
            tasks,
            counts,
            held is null ? null : new MyCashHeld(held.Amount, held.ParcelCount, held.OldestCollectedAt),
            branchNames.TryGetValue(rider.BaseBranchId, out var branchName) ? branchName : null,
            plates.OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault());
    }
}
