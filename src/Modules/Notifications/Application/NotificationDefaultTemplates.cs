namespace Courier.Notifications.Application;

// Fallback bodies used when no active NotificationTemplate row exists for an
// (OrganizationId, EventType, Channel) combination — which is every combination this phase,
// since nothing seeds template rows (Task 5's resolved implementation detail). Only Email has
// defaults, since Email is the only channel this phase actually dispatches through (see
// NotificationDispatchService.DispatchEventAsync — it only attempts a channel it has a
// resolvable body for). {PlaceholderName} tokens are replaced from the event payload's own
// top-level JSON properties by DispatchEventAsync — this file only owns the text.
public static class NotificationDefaultTemplates
{
    public static readonly IReadOnlyDictionary<string, string> Email = new Dictionary<string, string>
    {
        ["shipment.created"] = "Your shipment {Awb} has been created and is being processed.",
        ["shipment.delivered"] = "Your shipment {Awb} has been delivered.",
        ["shipment.ndr_created"] = "We were unable to deliver shipment {Awb}. Our team will contact you shortly.",
        ["cod.collected"] = "COD amount of Rs. {CollectedAmount} has been collected for shipment {ShipmentId}."
    };
}
