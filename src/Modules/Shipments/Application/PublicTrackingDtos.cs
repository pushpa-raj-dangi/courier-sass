using System.Text.Json.Serialization;

namespace Courier.Shipments.Application;

// CLAUDE.md §24's safe subset only: a status label, a timestamp, and an optional city/hub-level
// location. No branch ID, address, coordinates, staff/rider identity, notes, financial field, or
// recipient PII beyond what the requester (holder of the AWB) already possesses.
// [JsonConverter(typeof(JsonStringEnumConverter))] — same recurring fix as ShipmentStatus,
// BranchType, ManifestStatus/ManifestPurpose, the three Ndr enums, and the three Cod enums: no
// global JsonStringEnumConverter is registered for the API, so every enum serializes as a raw
// integer unless individually opted in.
[JsonConverter(typeof(JsonStringEnumConverter))]
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
