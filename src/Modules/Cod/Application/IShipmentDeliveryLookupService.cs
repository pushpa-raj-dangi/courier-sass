namespace Courier.Cod.Application;

// Same dependency-inversion shape as IShipmentAwbLookupService: Cod cannot reference Shipments, so
// Cod declares what it needs and Shipments.Application implements it.
public interface IShipmentDeliveryLookupService
{
    // Distinct shipments of the merchant with a Delivered or DeliveredAbroad event in [fromUtc, toUtc).
    Task<int> CountDeliveredAsync(Guid merchantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    // True when the (tenant-filtered) shipment exists and belongs to the merchant.
    Task<bool> BelongsToMerchantAsync(Guid shipmentId, Guid merchantId, CancellationToken ct = default);
}
