namespace Courier.Shipments.Application;

// CLAUDE.md §24's safe subset only: a status label, a timestamp, and an optional city/hub-level
// location. No branch ID, address, coordinates, staff/rider identity, notes, financial field, or
// recipient PII beyond what the requester (holder of the AWB) already possesses.
public enum PublicTrackingStatus
{
    Received,
    PickedUp,
    InTransit,
    AtDestinationBranch,
    OutForDelivery,
    Delivered,
    DeliveryAttemptUnsuccessful,
    Cancelled
}

public record PublicTrackingEvent(PublicTrackingStatus Status, DateTime Timestamp, string? Location);

public record PublicTrackingResponse(string Awb, PublicTrackingStatus CurrentStatus, List<PublicTrackingEvent> Events);
