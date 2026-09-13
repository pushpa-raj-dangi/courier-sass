using System.Security.Claims;
using Courier.Ndr.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public record ReturnShipmentSummary(
    IReadOnlyDictionary<ShipmentStatus, int> ByRtoStage, int MerchantConfirmed, int PendingConfirmation);

public partial class NdrService
{
    // Scoped the same way ListReturnShipmentsAsync already is: ReturnShipment stores
    // DestinationBranchId as a real column (despite the name, per ReturnShipment.cs's own doc
    // comment this is where the parcel ends up after RtoAtOrigin -- the original shipment's
    // origin branch), so the filter pushes straight into SQL, no join needed for scoping.
    // CurrentStatus isn't stored on ReturnShipment itself though, so grouping by RTO stage still
    // needs the same per-row GetScanInfoAsync lookup ListReturnShipmentsAsync already pays for
    // every row it returns -- no new performance characteristic, just reused here.
    public async Task<ReturnShipmentSummary> GetReturnShipmentSummaryAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rto.read", cancellationToken);

        var query = _dbContext.Set<ReturnShipment>().AsNoTracking().AsQueryable();
        if (accessibleBranchIds is not null)
        {
            var branchIdArray = accessibleBranchIds.ToArray();
            query = query.Where(r => branchIdArray.Contains(r.DestinationBranchId));
        }

        var returnShipments = await query.ToListAsync(cancellationToken);
        var merchantConfirmed = returnShipments.Count(r => r.MerchantConfirmedAt is not null);

        var byRtoStage = new Dictionary<ShipmentStatus, int>();
        foreach (var returnShipment in returnShipments)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(returnShipment.OriginalShipmentId, cancellationToken);
            var status = scanInfo?.CurrentStatus ?? ShipmentStatus.RtoInitiated;
            byRtoStage[status] = byRtoStage.GetValueOrDefault(status) + 1;
        }

        return new ReturnShipmentSummary(byRtoStage, merchantConfirmed, returnShipments.Count - merchantConfirmed);
    }
}
