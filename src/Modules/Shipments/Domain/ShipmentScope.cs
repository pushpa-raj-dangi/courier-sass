using System.Text.Json.Serialization;

namespace Courier.Shipments.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShipmentScope
{
    Domestic,
    International
}
