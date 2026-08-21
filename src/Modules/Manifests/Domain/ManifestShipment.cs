namespace Courier.Manifests.Domain;

// A null ReceivedAt on a Closed manifest IS the missing-shipment flag — no separate IsMissing
// column (this plan's resolved design decision #4).
public class ManifestShipment
{
    public Guid Id { get; set; }
    public Guid ManifestId { get; set; }
    public Guid ShipmentId { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
}
