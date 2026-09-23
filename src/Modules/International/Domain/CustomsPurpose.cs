using System.Text.Json.Serialization;

namespace Courier.International.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomsPurpose
{
    Gift,
    CommercialSale,
    Sample,
    Return,
    Documents,
    Other
}
