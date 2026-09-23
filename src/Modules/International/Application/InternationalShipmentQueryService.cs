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

    // Reads InternationalLeg directly (International owns this table) but reaches the Shipment
    // side only through IShipmentTransitionService.GetScanInfoAsync — that projection is narrower
    // than a full ShipmentResponse, so a fuller read here goes through
    // IShipmentInternationalGateway's sibling responses instead: this method returns the last
    // ShipmentResponse produced by a gateway call plus the live InternationalLeg row is NOT
    // possible without re-fetching Shipment fields, so GetAsync instead composes from
    // ShipmentScanInfo (status/branches) — callers needing full recipient/sender/pricing detail
    // use the response returned inline by CreateAsync/PrepareCustomsAsync instead. This keeps
    // International's read path within the "never touch Shipment's DbSet" rule using only
    // interfaces that already exist (Task 6/8's IShipmentInternationalGateway has no standalone
    // "get" method — none was added since nothing in this plan's scope needs a second full read).
    //
    // This task (9) implements only the leg-only read below, GetLegAsync — Task 11 replaces it
    // with a merged GetAsync(Guid, CancellationToken) once IShipmentTransitionService.GetResponseAsync
    // exists to supply the Shipment side (see task-9-brief.md's note for the implementer).
    // _shipmentTransitionService is unused by GetLegAsync; it is accepted here so Task 11 can add
    // GetAsync without a breaking constructor-signature change.
    public async Task<InternationalLegResponse?> GetLegAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        return new InternationalLegResponse(
            leg.ShipmentId, leg.ContentsDescription, leg.CountryOfOrigin, leg.Purpose.ToString(), leg.Incoterm.ToString(),
            leg.ExportLicenceReference, leg.CustomsCompletedAt is not null, leg.ProvisionalTotalChargeAmount, leg.ProvisionalTotalChargeCurrency, leg.IsProvisionalPricing);
    }
}
