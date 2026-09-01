using System.Text.Json.Serialization;

namespace Courier.Ndr.Domain;

// Deliberately duplicated from Riders.Domain.DeliveryAttemptReason (identical 9 values, CLAUDE.md
// §19) rather than referenced — avoids Ndr.csproj needing a project reference to Riders.csproj
// purely for one enum's type, mirroring how Riders.Domain.DeliveryAttemptReason was itself already
// a fresh copy of CLAUDE.md §19's list, not shared from anywhere else. Riders.Application.
// RiderService converts its own DeliveryAttemptReason to this one via an explicit switch
// expression (Task 6), not a raw int cast — so a future divergence between the two lists fails
// loudly at compile time instead of silently miscasting.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NdrReason
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
