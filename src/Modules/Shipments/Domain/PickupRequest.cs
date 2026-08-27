namespace Courier.Shipments.Domain;

// BranchId is resolved from the requested shipments' own OriginBranchId at creation
// (PickupRequestService, Task 9) — every ShipmentId in one request must share one
// OriginBranchId, so this column is a validated denormalization, not caller-supplied input.
// No rider/DeliveryRun linkage yet (spec §3 Non-Goals) — Status only ever reaches Cancelled
// via the merchant this phase; Scheduled/Completed are modeled now so the column exists, but
// nothing transitions a row into them until the Phase 8 DeliveryRun wiring lands.
public class PickupRequest
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MerchantId { get; set; }
    public Guid BranchId { get; set; }
    public required Guid[] ShipmentIds { get; set; }
    public DateOnly RequestedForDate { get; set; }
    public string? Notes { get; set; }
    public PickupRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
