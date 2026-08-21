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
    Cancelled
}
