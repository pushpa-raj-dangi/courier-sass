using System.Security.Claims;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService
{
    public async Task<ShipmentResponse?> GetByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && shipment.MerchantId != callerMerchantId)
        {
            return null;
        }

        return await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    public async Task<List<ShipmentResponse>> ListAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Shipment>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(s => s.MerchantId == callerMerchantId);
        }
        else
        {
            // Staff caller: scope to branches the caller can access for shipment.read. null means
            // "no filter applies" (BranchScope.AllBranches) per IBranchAuthorizationChecker's
            // documented contract — a non-null set restricts to shipments touching any of those
            // branches as origin, destination, or current physical location. CurrentBranchId is
            // nullable (in-transit/with-rider shipments), so a restricted-branch caller can still
            // see such a shipment via its origin/destination even while CurrentBranchId is null.
            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "shipment.read", cancellationToken);
            if (accessibleBranchIds is not null)
            {
                query = query.Where(s =>
                    accessibleBranchIds.Contains(s.OriginBranchId) ||
                    accessibleBranchIds.Contains(s.DestinationBranchId) ||
                    (s.CurrentBranchId != null && accessibleBranchIds.Contains(s.CurrentBranchId.Value)));
            }
        }

        var shipments = await query.ToListAsync(cancellationToken);
        var responses = new List<ShipmentResponse>();
        foreach (var shipment in shipments)
        {
            var items = await _dbContext.Set<ShipmentItem>().Where(i => i.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
            responses.Add(ToResponse(shipment, items));
        }
        return responses;
    }
}
