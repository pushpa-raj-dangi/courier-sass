namespace Courier.Notifications.Domain;

public enum NotificationStatus
{
    // A real outbound send succeeded (this phase: Email only).
    Sent,
    // A stub channel (Sms/WhatsApp/Push) "handled" the notification by logging it — no real
    // outbound call was made, by design this phase (spec §2).
    Logged,
    // A real outbound send was attempted and failed (e.g. SMTP rejected it).
    Failed,
    // No recipient address could be resolved (e.g. Merchant.ContactEmail is null) — an
    // expected, non-exceptional org data state, not a pipeline failure (spec §6).
    NoRecipient
}
