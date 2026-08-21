namespace Courier.Riders.Domain;

// Exact list from CLAUDE.md §19.
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
