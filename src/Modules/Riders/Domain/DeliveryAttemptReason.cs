using System.Text.Json.Serialization;

namespace Courier.Riders.Domain;

// Exact list from CLAUDE.md §19.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeliveryAttemptReason
{
    CustomerUnavailable,
    PhoneUnreachable,
    WrongAddress,
    IncompleteAddress,
    CustomerRefused,
    CustomerRequestedReschedule,
    CashUnavailable,
    DamagedParcel,
    Other
}
