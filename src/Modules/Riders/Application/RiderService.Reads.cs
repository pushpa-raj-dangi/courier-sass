using System.Security.Claims;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    // Self-scoped by design (Phase F5's rider self-service model) — a rider holds no permission
    // that would let them call ListAsync/GetByIdAsync to find their own Rider.Id, so this is the
    // only path from a logged-in rider's UserId to their Rider record.
    public async Task<RiderResponse?> GetMyRiderAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var userId = actingUser.GetUserId();
        if (userId is null)
        {
            return null;
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        return rider is null ? null : ToResponse(rider);
    }

    // Sequential awaits, not Task.WhenAll — EF Core's DbContext is not thread-safe, and Phase 13
    // hit a real InvalidOperationException from exactly this shape of concurrent-await-on-one-
    // DbContext mistake.
    public async Task<MyTasksTodayResponse> GetMyTasksTodayAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var userId = actingUser.GetUserId();
        if (userId is null)
        {
            return new MyTasksTodayResponse(null, new List<MyTaskResponse>());
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return new MyTasksTodayResponse(null, new List<MyTaskResponse>());
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var run = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(
            r => r.RiderId == rider.Id && r.Date == today && r.Status == DeliveryRunStatus.Active, cancellationToken);
        if (run is null)
        {
            return new MyTasksTodayResponse(null, new List<MyTaskResponse>());
        }

        var pendingTasks = await _dbContext.Set<DeliveryTask>()
            .Where(t => t.DeliveryRunId == run.Id && t.Status == DeliveryTaskStatus.Pending)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);

        var results = new List<MyTaskResponse>();
        foreach (var task in pendingTasks)
        {
            var info = await _shipmentTaskLookupService.GetTaskInfoAsync(task.ShipmentId, cancellationToken);
            if (info is null)
            {
                continue;
            }
            results.Add(new MyTaskResponse(task.Id, task.ShipmentId, task.Type, task.Sequence, info.Awb, info.RecipientName, info.RecipientPhone, info.RecipientAddressLine, info.RecipientWardNumber, info.CodAmount));
        }
        return new MyTasksTodayResponse(run.Id, results);
    }

    public async Task<RiderResponse?> GetByIdAsync(Guid riderId, CancellationToken cancellationToken = default)
    {
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken);
        return rider is null ? null : ToResponse(rider);
    }

    // Branch-scoped the same way ShipmentService.ListAsync (Phase 6) and BranchService.ListAsync
    // (Phase 2) already are: null from GetAccessibleBranchIdsAsync means AllBranches (no filter);
    // a non-null set restricts to riders based at one of those branches.
    public async Task<List<RiderResponse>> ListAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Rider>().AsQueryable();
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rider.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
            query = query.Where(r => accessibleBranchIdsArray.Contains(r.BaseBranchId));
        }

        return await query
            .Select(r => new RiderResponse(r.Id, r.UserId, r.BaseBranchId, r.LicenseNumber, r.RoleType, r.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<VehicleResponse>> ListVehiclesAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Vehicle>()
            .Select(v => new VehicleResponse(v.Id, v.RiderId, v.Type, v.PlateNumber, v.Capacity))
            .ToListAsync(cancellationToken);
    }
}
