using System.Text.Json.Serialization;

namespace Courier.Billing.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InvoiceStatus
{
    Pending = 0,
    Paid = 1,
    Overdue = 2,
    Cancelled = 3
}
