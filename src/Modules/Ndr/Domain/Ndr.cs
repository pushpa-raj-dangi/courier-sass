namespace Courier.Ndr.Domain;

// Auto-created by Ndr.Application.NdrCreationService.CreateFromFailedAttemptAsync, called from
// Riders.Application.RiderService.RecordFailedAttemptAsync immediately after FailDeliveryAsync
// (Task 6) — SHIPMENT-LIFECYCLE.md's diagram shows DELIVERY_FAILED -> NDR_CREATED as an automatic
// system step, not a separate manual action.
public class Ndr
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ShipmentId { get; set; }
    public NdrReason Reason { get; set; }
    public Guid? AssignedStaffUserId { get; set; }
    public NdrStatus Status { get; set; }
    public string? CustomerResponse { get; set; }
    public DateTimeOffset? FollowUpAt { get; set; }
    public string? NextAction { get; set; }
    public NdrResolution? Resolution { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
