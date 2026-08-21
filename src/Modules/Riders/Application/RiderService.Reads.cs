using System.Security.Claims;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
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
