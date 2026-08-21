namespace Courier.Shipments.Domain;

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
    NdrCreated,
    RtoInitiated,
    RtoInTransit,
    RtoAtOrigin,
    RtoDelivered,
    Delivered,
    Cancelled
}
