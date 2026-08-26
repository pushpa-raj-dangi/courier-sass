namespace Courier.Notifications.Domain;

// An org-specific override for one (EventType, Channel) pair. No row existing for a given
// combination is normal and expected this phase — NotificationDispatchService (Task 6) falls
// back to a compiled-in default body when no active row matches. A future template-management
// API (deferred, spec §2) would let an org create/edit these.
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EventType { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Body { get; set; }
    public bool IsActive { get; set; }
}
