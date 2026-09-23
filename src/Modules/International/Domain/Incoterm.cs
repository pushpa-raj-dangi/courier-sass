using System.Text.Json.Serialization;

namespace Courier.International.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Incoterm
{
    Ddu,
    Ddp
}
