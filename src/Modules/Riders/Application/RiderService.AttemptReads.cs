using System.Security.Claims;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    // Lives in Riders (the DeliveryAttempt owner) because Ndr cannot reference Riders — Riders
    // already references Ndr. Scope: the shipment's current, origin or destination branch must be
    // in the caller's ndr.read scope, the same rule as the staff shipment single read; out of
    // scope reads as not found. Newest first; not paginated (attempts per shipment are few).
    public async Task<DeliveryAttemptListResponse?> ListDeliveryAttemptsAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scanInfo is null)
        {
            return null;
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "ndr.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var inScope = accessibleBranchIds.Contains(scanInfo.OriginBranchId) ||
                          accessibleBranchIds.Contains(scanInfo.DestinationBranchId) ||
                          (scanInfo.CurrentBranchId is Guid currentBranchId && accessibleBranchIds.Contains(currentBranchId));
            if (!inScope)
            {
                return null;
            }
        }

        var items = await _dbContext.Set<DeliveryAttempt>().AsNoTracking()
            .Where(a => a.ShipmentId == shipmentId)
            .OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.AttemptNumber)
            .Select(a => new DeliveryAttemptHistoryItem(
                a.AttemptNumber, a.AttemptedAt, a.Succeeded, a.Reason, a.Notes, a.CustomerContacted, a.NextAction, a.RiderId))
            .ToListAsync(cancellationToken);
        return new DeliveryAttemptListResponse(items);
    }
}
