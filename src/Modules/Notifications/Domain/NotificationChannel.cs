using System.Text.Json.Serialization;

namespace Courier.Notifications.Domain;

// Exposed over JSON for the first time by the Notification Templates API (Task 7) — without this
// attribute it would silently serialize as a raw integer, the same bug this codebase has already
// hit on BranchType, ShipmentStatus, ManifestPurpose/ManifestStatus, three Ndr enums, three Cod
// enums, and SubscriptionStatus.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationChannel
{
    Sms,
    Email,
    WhatsApp,
    Push
}
