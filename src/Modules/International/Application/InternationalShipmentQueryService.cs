using Courier.International.Domain;
using Courier.Shipments.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

public class InternationalShipmentQueryService
{
    private readonly DbContext _dbContext;
    private readonly IShipmentTransitionService _shipmentTransitionService;

    public InternationalShipmentQueryService(DbContext dbContext, IShipmentTransitionService shipmentTransitionService)
    {
        _dbContext = dbContext;
        _shipmentTransitionService = shipmentTransitionService;
    }

    // Reads InternationalLeg directly (International owns this table) and merges in the full
    // Shipment side via IShipmentTransitionService.GetResponseAsync (added in Task 11) — this keeps
    // International's read path within the "never touch Shipment's DbSet" rule while still
    // returning the full recipient/sender/pricing detail GET callers need, not just the narrower
    // ShipmentScanInfo projection used by scan-validation callers elsewhere.
    public async Task<InternationalShipmentDetailResponse?> GetAsync(Guid shipmentId, CancellationToken cancellationToken = default)
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
        return new InternationalShipmentDetailResponse(shipment, new InternationalLegResponse(
            leg.ShipmentId, leg.ContentsDescription, leg.CountryOfOrigin, leg.Purpose.ToString(), leg.Incoterm.ToString(),
            leg.ExportLicenceReference, leg.CustomsCompletedAt is not null, leg.ProvisionalTotalChargeAmount, leg.ProvisionalTotalChargeCurrency, leg.IsProvisionalPricing));
    }
}
