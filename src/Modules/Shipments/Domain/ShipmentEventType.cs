using System.Text.Json.Serialization;

namespace Courier.Shipments.Domain;

// Serialised as its PascalCase name (like ShipmentStatus) for the staff events endpoint; EF column
// mapping is untouched, so the persisted-ordinal rule still applies.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShipmentEventType
{
    Created,
    PickupAssigned,
    PickedUp,
    ReceivedAtOrigin,
    Sorted,
    Dispatched,
    ReceivedAtDestination,
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
