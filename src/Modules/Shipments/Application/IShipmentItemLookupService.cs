namespace Courier.Shipments.Application;

// Lets International.Application read a shipment's items (to build CustomsItem rows) without
// International ever calling _dbContext.Set<ShipmentItem>() directly — same precedent as
// IShipmentInternationalGateway.
public record ShipmentItemRecord(Guid Id, string Description, int Quantity, decimal UnitValue);

public interface IShipmentItemLookupService
{
    Task<IReadOnlyList<ShipmentItemRecord>> GetItemsAsync(Guid shipmentId, CancellationToken cancellationToken = default);
}
