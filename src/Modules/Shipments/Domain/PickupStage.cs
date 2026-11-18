using System.Text.Json.Serialization;

namespace Courier.Shipments.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PickupStage
{
    Requested,
    Scheduled,
    Collected,
    Cancelled
}

// Derives a pickup's stage at read time from its parcels; no stored stage state.
public static class PickupStageRules
{
    // Past pickup means every status except Created, PickupAssigned and Cancelled. Written as an
    // exclusion so that any status added later defaults to "past pickup".
    public static bool IsPastPickup(ShipmentStatus status) =>
        status is not (ShipmentStatus.Created or ShipmentStatus.PickupAssigned or ShipmentStatus.Cancelled);

    public static PickupStage Derive(
        PickupRequestStatus stored,
        IReadOnlyCollection<ShipmentStatus> parcels)
    {
        if (stored == PickupRequestStatus.Cancelled)
        {
            return PickupStage.Cancelled;
        }

        if (stored == PickupRequestStatus.Completed)
        {
            return PickupStage.Collected;
        }

        var active = parcels.Where(p => p != ShipmentStatus.Cancelled).ToList();

        if (active.Count == 0)
        {
            return PickupStage.Requested;
        }

        if (active.All(IsPastPickup))
        {
            return PickupStage.Collected;
        }

        if (active.Any(p => p == ShipmentStatus.PickupAssigned || IsPastPickup(p)))
        {
            return PickupStage.Scheduled;
        }

        return PickupStage.Requested;
    }
}
