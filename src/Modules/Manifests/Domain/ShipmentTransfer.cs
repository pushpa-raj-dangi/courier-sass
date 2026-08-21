namespace Courier.Manifests.Domain;

// Append-only physical-custody record, one row per shipment at receive-time — distinct from
// Shipments.Domain.ShipmentEvent per SHIPMENT-LIFECYCLE.md §5.
public class ShipmentTransfer
{
    public Guid Id { get; set; }
    public Guid ManifestId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
