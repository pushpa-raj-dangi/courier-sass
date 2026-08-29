using System.Text.Json.Serialization;

namespace Courier.Branches.Domain;

// No global JsonStringEnumConverter is registered for the API (other enums still serialize as
// integers) — this attribute scopes the string contract to just this enum, matching what the
// frontend's BranchType union sends/expects (types.ts), without touching unrelated modules.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BranchType
{
    HeadOffice,
    Hub,
    Branch,
    PickupPoint,
    Warehouse
}
