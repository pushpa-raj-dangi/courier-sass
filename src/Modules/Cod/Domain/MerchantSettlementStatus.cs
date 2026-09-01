using System.Text.Json.Serialization;

namespace Courier.Cod.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MerchantSettlementStatus
{
    Created,
    Settled
}
