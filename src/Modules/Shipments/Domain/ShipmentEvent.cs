namespace Courier.Shipments.Domain;

// Append-only — no update/delete anywhere in the API surface. PreviousStatus is null only for
// the very first (Created) event. RiderId is always null this phase (no Rider entity yet).
public class ShipmentEvent
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public ShipmentEventType EventType { get; set; }
    public ShipmentStatus? PreviousStatus { get; set; }
    public ShipmentStatus NewStatus { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? RiderId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Notes { get; set; }
    public string? Metadata { get; set; }
}
