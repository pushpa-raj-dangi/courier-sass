using System.Text.Json.Serialization;

namespace Courier.Merchants.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MerchantUserRole
{
    Owner,
    Staff
}
