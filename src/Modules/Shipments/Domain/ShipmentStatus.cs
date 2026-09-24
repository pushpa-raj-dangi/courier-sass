using System.Text.Json.Serialization;

namespace Courier.Shipments.Domain;

// No global JsonStringEnumConverter is registered for the API (other enums still serialize as
// integers) — this attribute scopes the string contract to just this enum, matching what the
// frontend's StatusBadge/statusConfig.ts expects (PascalCase names like "Created", not integers).
// Serialization-only: EF Core's database column mapping is untouched, so CLARIFICATIONS.md #10's
// append-only-ordinal rule for the persisted integer still applies unchanged.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShipmentStatus
{
    Created,
    PickupAssigned,
    PickedUp,
    AtOriginBranch,
    Sorted,
    InTransit,
    AtDestinationBranch,
    OutForDelivery,
    DeliveryFailed,
    Delivered,
    Cancelled,
    NdrCreated,
    RtoInitiated,
    RtoInTransit,
    RtoAtOrigin,
    RtoDelivered,
    CustomsPrepared,
    // I4 — international carrier lifecycle. Append-only (persisted ordinals).
    HandedToCarrier,
    InInternationalTransit,
    DeliveredAbroad,
    CustomsHold,
    InternationalException,
    CustomsClearance,
    ReceivedFromCarrier
}
