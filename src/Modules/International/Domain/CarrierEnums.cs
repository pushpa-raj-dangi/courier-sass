namespace Courier.International.Domain;

// International's own direction enum (not Shipments.Domain.ShipmentDirection) so this module's
// domain does not depend on another module's domain. Mapped in International.Application.
public enum CarrierBookingDirection
{
    Outbound,
    Inbound
}

public enum CarrierBookingStatus
{
    Pending,
    Booked,
    Failed,
    Cancelled
}

public enum CarrierEventSource
{
    Webhook,
    Poll,
    Manual
}

// Processing is the processor's atomic claim (plan ruling) — an event is only ever applied by
// the one run that moved it Pending -> Processing.
public enum CarrierEventProcessingStatus
{
    Pending,
    Processing,
    Applied,
    Ignored,
    Rejected,
    Unmatched
}
