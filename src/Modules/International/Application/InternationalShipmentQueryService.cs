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
        var legResponse = await InternationalShipmentAccess.ToLegResponseAsync(_dbContext, leg, cancellationToken);
        var bookings = await _dbContext.Set<CarrierBooking>().Where(b => b.InternationalLegId == leg.Id)
            .OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken);
        if (bookings.Count == 0)
        {
            return new InternationalShipmentDetailResponse(shipment, legResponse, null, Array.Empty<CarrierBookingResponse>(), Array.Empty<CarrierTrackingEventResponse>());
        }

        var accountIds = bookings.Select(b => b.CarrierAccountId).Distinct().ToList();
        var accounts = await _dbContext.Set<CarrierAccount>().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        var bookingResponses = bookings.Select(b => CarrierBookingService.ToResponse(b, accounts[b.CarrierAccountId])).ToList();
        var live = bookings.FirstOrDefault(b => b.Status is CarrierBookingStatus.Pending or CarrierBookingStatus.Booked);

        // Every event reported for any of this shipment's (account, carrier AWB) pairs — including
        // ones still Pending/Unmatched, so staff see carrier activity before it is applied.
        var pairs = bookings.Where(b => b.CarrierAwb is not null).Select(b => (b.CarrierAccountId, b.CarrierAwb!)).ToHashSet();
        var awbs = pairs.Select(p => p.Item2).Distinct().ToList();
        var events = (await _dbContext.Set<CarrierTrackingEvent>()
                .Where(e => accountIds.Contains(e.CarrierAccountId) && awbs.Contains(e.CarrierAwb))
                .OrderBy(e => e.OccurredAt)
                .ToListAsync(cancellationToken))
            .Where(e => pairs.Contains((e.CarrierAccountId, e.CarrierAwb)))
            .Select(CarrierTrackingEventResponse.From)
            .ToList();

        return new InternationalShipmentDetailResponse(shipment, legResponse,
            live is null ? null : CarrierBookingService.ToResponse(live, accounts[live.CarrierAccountId]), bookingResponses, events);
    }
}
