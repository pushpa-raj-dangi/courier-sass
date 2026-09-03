using System.Text.Json.Serialization;

namespace Courier.Shipments.Domain;

// Same recurring gap — this enum was never revisited since Phase 12 introduced it, and no
// frontend phase touched Pickups until now (Frontend Phase F10's merchant-pickups screen is the
// first UI to render PickupRequestResponse.Status).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PickupRequestStatus
{
    Requested,
    Scheduled,
    Completed,
    Cancelled
}
