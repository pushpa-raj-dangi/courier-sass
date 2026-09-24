using System.Security.Claims;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

public class InternationalShipmentQueryService
{
    private readonly DbContext _dbContext;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public InternationalShipmentQueryService(DbContext dbContext, IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    // Reads InternationalLeg/CustomsItem directly (International owns these tables) and merges in
    // the full Shipment side via IShipmentTransitionService.GetResponseAsync — this keeps
    // International's read path within the "never touch Shipment's DbSet" rule while still
    // returning the full recipient/sender/pricing detail GET callers need. Null (-> 404) when the
    // leg or shipment is outside the caller's tenant, or outside their branch scope for
    // international.read.
    public async Task<InternationalShipmentDetailResponse?> GetAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var shipment = await _shipmentTransitionService.GetResponseAsync(shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (!await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, "international.read",
                shipment.OriginBranchId, shipment.DestinationBranchId, shipment.CurrentBranchId, cancellationToken))
        {
            return null;
        }
        return new InternationalShipmentDetailResponse(shipment, await InternationalShipmentAccess.ToLegResponseAsync(_dbContext, leg, cancellationToken));
    }
}
