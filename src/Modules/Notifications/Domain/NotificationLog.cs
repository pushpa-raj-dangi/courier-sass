namespace Courier.Notifications.Domain;

// One row per dispatch attempt, written unconditionally (success, failure, or no-recipient) by
// NotificationDispatchService (Task 6) — this is the durable record CLAUDE.md §27's pipeline
// diagram implies ("Notification Worker -> SMS/Email/WhatsApp/Push") without itself being
// itemized as a requirement anywhere; it exists because a dispatch attempt with no trace
// anywhere would be undebuggable.
public class NotificationLog
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EventType { get; set; }
    public NotificationChannel Channel { get; set; }
    public string? RecipientAddress { get; set; }
    public NotificationStatus Status { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string? ProviderResponse { get; set; }
}
