using System.Text.Json.Serialization;

namespace Courier.Merchants.Domain;

// See Branches.Domain.BranchType for why this is a per-enum attribute rather than a global converter.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KycStatus
{
    Pending,
    Approved,
    Rejected
}
