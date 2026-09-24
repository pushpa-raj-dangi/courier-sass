using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public static class PublicTrackingStatusMapper
{
    public static PublicTrackingStatus Map(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created or ShipmentStatus.PickupAssigned => PublicTrackingStatus.Received,
        ShipmentStatus.PickedUp => PublicTrackingStatus.PickedUp,
        ShipmentStatus.AtOriginBranch or ShipmentStatus.Sorted or ShipmentStatus.InTransit or ShipmentStatus.CustomsPrepared
            or ShipmentStatus.HandedToCarrier or ShipmentStatus.InInternationalTransit or ShipmentStatus.CustomsClearance
            or ShipmentStatus.ReceivedFromCarrier
            // Exceptions are internal (I4 spec §3.7) — the public sees the parcel as still moving.
            or ShipmentStatus.CustomsHold or ShipmentStatus.InternationalException
            => PublicTrackingStatus.InTransit,
        ShipmentStatus.AtDestinationBranch => PublicTrackingStatus.AtDestinationBranch,
        ShipmentStatus.OutForDelivery => PublicTrackingStatus.OutForDelivery,
        ShipmentStatus.Delivered or ShipmentStatus.DeliveredAbroad => PublicTrackingStatus.Delivered,
        ShipmentStatus.DeliveryFailed or ShipmentStatus.NdrCreated or ShipmentStatus.RtoInitiated
            or ShipmentStatus.RtoInTransit or ShipmentStatus.RtoAtOrigin or ShipmentStatus.RtoDelivered
            => PublicTrackingStatus.DeliveryAttemptUnsuccessful,
        ShipmentStatus.Cancelled => PublicTrackingStatus.Cancelled,
        _ => throw new NotSupportedException($"ShipmentStatus '{status}' has no public tracking mapping.")
    };
}
