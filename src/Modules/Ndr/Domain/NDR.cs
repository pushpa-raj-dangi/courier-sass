namespace Courier.Ndr.Domain;

// Named NDR (all-caps, matching DOMAIN-MODEL.md's literal spelling) rather than the Pascal-cased
// "Ndr" every other entity in this module uses — deliberately: "Ndr" as a class name inside
// `namespace Courier.Ndr.*` collides with the enclosing namespace segment (C# resolves the bare
// name to the namespace, not the type, producing CS0118 "is a namespace but is used like a type").
// NDR's all-caps spelling is a different identifier under C#'s case-sensitive rules, so it never
// collides.
//
// Auto-created by Ndr.Application.NdrCreationService.CreateFromFailedAttemptAsync, called from
// Riders.Application.RiderService.RecordFailedAttemptAsync immediately after FailDeliveryAsync
// (Task 6) — SHIPMENT-LIFECYCLE.md's diagram shows DELIVERY_FAILED -> NDR_CREATED as an automatic
// system step, not a separate manual action.
public class NDR
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
