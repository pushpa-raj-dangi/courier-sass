namespace Courier.Shipments.Domain;

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
