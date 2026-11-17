using System.Text.Json.Serialization;

namespace Courier.Shipments.Application;

// CLAUDE.md §24's safe subset only: a status label, a timestamp, and an optional city/hub-level
// location. No branch ID, address, coordinates, staff/rider identity, notes, financial field, or
// recipient PII beyond what the requester (holder of the AWB) already possesses.
// [JsonConverter(typeof(JsonStringEnumConverter))] — same recurring fix as ShipmentStatus,
// BranchType, ManifestStatus/ManifestPurpose, the three Ndr enums, and the three Cod enums: no
// global JsonStringEnumConverter is registered for the API, so every enum serializes as a raw
// integer unless individually opted in.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PublicTrackingStatus
{
    Received,
    PickedUp,
    InTransit,
    AtDestinationBranch,
    OutForDelivery,
    Delivered,
    DeliveryAttemptUnsuccessful,
    Cancelled,
    // Appended (not inserted) so existing member ordinals stay stable.
    ReturningToSender
}

public record PublicTrackingEvent(PublicTrackingStatus Status, DateTime Timestamp, string? Location);

public record PublicTrackingResponse(string Awb, PublicTrackingStatus CurrentStatus, List<PublicTrackingEvent> Events,
    string? DestinationArea, string? SenderName);

// Coarse destination only (municipality/district, or the free-text city for international
// shipments) — never ward, street address, or phone. Geography wins over the free-text city.
public static class PublicTrackingAreaFormatter
{
    public static string? Format(string? localLevelName, string? districtName, string? city)
    {
        var parts = new[] { localLevelName, districtName }
            .Select(p => p?.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();
        if (parts.Length > 0)
        {
            return string.Join(", ", parts);
        }
        var trimmedCity = city?.Trim();
        return string.IsNullOrEmpty(trimmedCity) ? null : trimmedCity;
    }
}
