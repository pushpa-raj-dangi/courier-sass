using Courier.Shipments.Domain;

namespace Courier.International.Application.Carriers;

// The statuses in which a shipment is in the carrier's hands (or, inbound, not yet received by
// us), so carrier tracking is still meaningful and polling is worth doing.
public static class CarrierTrackingWindow
{
    public static bool IsTracked(ShipmentDirection direction, ShipmentStatus status) => direction switch
    {
        ShipmentDirection.Outbound => status is ShipmentStatus.HandedToCarrier or ShipmentStatus.InInternationalTransit
            or ShipmentStatus.CustomsHold or ShipmentStatus.InternationalException,
        ShipmentDirection.Inbound => status is ShipmentStatus.Created or ShipmentStatus.InInternationalTransit
            or ShipmentStatus.CustomsClearance or ShipmentStatus.CustomsHold or ShipmentStatus.InternationalException,
        _ => false
    };
}
