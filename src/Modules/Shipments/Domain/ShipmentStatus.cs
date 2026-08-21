namespace Courier.Shipments.Domain;

// NDR/RTO states (RtoInitiated, RtoInTransit, etc.) are added in Phase 9 — this phase only
// models the happy path plus Cancelled (ROADMAP.md Phase 6).
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
    Cancelled
}
