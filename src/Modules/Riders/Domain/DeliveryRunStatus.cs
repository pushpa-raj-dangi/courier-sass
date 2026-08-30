using System.Text.Json.Serialization;

namespace Courier.Riders.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeliveryRunStatus
{
    Active,
    Completed
}
