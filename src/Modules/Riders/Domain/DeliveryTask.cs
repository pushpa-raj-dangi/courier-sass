namespace Courier.Riders.Domain;

// A Pickup task completes when RiderService.RiderPickupScanAsync succeeds (Task 8). A Delivery
// task completes/fails when the eventual DeliveryAttempt resolves (Task 9) — assigning a delivery
// task already performs the AtDestinationBranch -> OutForDelivery scan in the same call (see
// ShipmentService.Transitions.cs's design note, Task 2), so "assigned" and "in progress" are the
// same state for a delivery task; there's no separate scan step left to move it out of Pending.
// No own OrganizationId — flows transitively through DeliveryRunId -> DeliveryRun -> Rider.OrganizationId.
public class DeliveryTask
{
    public Guid Id { get; set; }
    public Guid DeliveryRunId { get; set; }
    public Guid ShipmentId { get; set; }
    public DeliveryTaskType Type { get; set; }
    public int Sequence { get; set; }
    public DeliveryTaskStatus Status { get; set; }
}
