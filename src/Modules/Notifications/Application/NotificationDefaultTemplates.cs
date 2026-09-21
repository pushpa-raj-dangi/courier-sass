namespace Courier.Notifications.Application;

// Fallback bodies used when no active NotificationTemplate row exists for an
// (OrganizationId, EventType, Channel) combination — which is every combination this phase,
// since nothing seeds template rows (Task 5's resolved implementation detail). Only Email has
// defaults, since Email is the only channel this phase actually dispatches through (see
// NotificationDispatchService.DispatchEventAsync — it only attempts a channel it has a
// resolvable body for). {PlaceholderName} tokens are replaced from the event payload's own
// top-level JSON properties by DispatchEventAsync — this file only owns the text.
// Supports locale-aware lookups via TryGetDefault(); locales are resolved by
// NotificationTemplateResolver which owns the fallback chain (this class is locale-specific only).
public static class NotificationDefaultTemplates
{
    public static readonly IReadOnlyDictionary<string, string> Email = new Dictionary<string, string>
    {
        ["shipment.created"] = "Your shipment {Awb} has been created and is being processed.",
        ["shipment.delivered"] = "Your shipment {Awb} has been delivered.",
        ["shipment.ndr_created"] = "We were unable to deliver shipment {Awb}. Our team will contact you shortly.",
        ["cod.collected"] = "COD amount of Rs. {CollectedAmount} has been collected for shipment {ShipmentId}.",
        ["user.invited"] = "You've been invited to join {OrganizationName} on Courier. Set your password: {InviteUrl}"
    };

    // Machine-drafted Nepali; needs native-speaker review (docs/INTERNATIONAL.md).
    public static readonly IReadOnlyDictionary<string, string> EmailNe = new Dictionary<string, string>
    {
        ["shipment.created"] = "तपाईंको ढुवानी {Awb} सिर्जना गरिएको छ र प्रक्रियामा छ।",
        ["shipment.delivered"] = "तपाईंको ढुवानी {Awb} डेलिभर गरिएको छ।",
        ["shipment.ndr_created"] = "हामी ढुवानी {Awb} डेलिभर गर्न सकेनौं। हाम्रो टोलीले छिट्टै तपाईंलाई सम्पर्क गर्नेछ।",
        ["cod.collected"] = "ढुवानी {ShipmentId} को लागि रु. {CollectedAmount} COD रकम संकलन गरिएको छ।",
        ["user.invited"] = "तपाईंलाई Courier मा {OrganizationName} मा सामेल हुन आमन्त्रित गरिएको छ। पासवर्ड सेट गर्नुहोस्: {InviteUrl}"
    };

    public static bool IsKnownEvent(string eventType) => Email.ContainsKey(eventType);

    // Locale-specific lookup with NO fallback — NotificationTemplateResolver owns the fallback chain.
    public static bool TryGetDefault(string locale, string eventType, out string body)
    {
        var source = locale switch
        {
            "en" => Email,
            "ne" => EmailNe,
            _ => null
        };
        if (source is not null && source.TryGetValue(eventType, out var found))
        {
            body = found;
            return true;
        }
        body = string.Empty;
        return false;
    }
}
