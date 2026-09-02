using System.Text.Json.Serialization;

namespace Courier.Pricing.Domain;

// See BranchType.cs for why this is a per-enum attribute rather than a global converter.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceTypeCode
{
    Standard,
    Express,
    SameDay,
    Heavy,
    Special
}
