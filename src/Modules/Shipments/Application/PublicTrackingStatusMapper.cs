using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public static class PublicTrackingStatusMapper
{
    public static PublicTrackingStatus Map(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created or ShipmentStatus.PickupAssigned => PublicTrackingStatus.Received,
        ShipmentStatus.PickedUp => PublicTrackingStatus.PickedUp,
        ShipmentStatus.AtOriginBranch or ShipmentStatus.Sorted or ShipmentStatus.InTransit => PublicTrackingStatus.InTransit,
        ShipmentStatus.AtDestinationBranch => PublicTrackingStatus.AtDestinationBranch,
        ShipmentStatus.OutForDelivery => PublicTrackingStatus.OutForDelivery,
        ShipmentStatus.Delivered => PublicTrackingStatus.Delivered,
        ShipmentStatus.DeliveryFailed or ShipmentStatus.NdrCreated or ShipmentStatus.RtoInitiated
            or ShipmentStatus.RtoInTransit or ShipmentStatus.RtoAtOrigin or ShipmentStatus.RtoDelivered
            => PublicTrackingStatus.DeliveryAttemptUnsuccessful,
        ShipmentStatus.Cancelled => PublicTrackingStatus.Cancelled,
        _ => throw new NotSupportedException($"ShipmentStatus '{status}' has no public tracking mapping.")
    };
}
