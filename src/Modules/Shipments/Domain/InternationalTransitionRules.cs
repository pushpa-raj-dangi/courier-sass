namespace Courier.Shipments.Domain;

public enum InternationalTransitionTrigger
{
    Carrier,
    StaffOverride
}

// The I4 spec §3.2 (outbound) / §3.3 (inbound) transition tables — the single place these moves
// are defined. Handoff (CustomsPrepared -> HandedToCarrier) and receive (-> ReceivedFromCarrier)
// are scan operations with their own gateway methods, so they are deliberately not in this table.
public static class InternationalTransitionRules
{
    private sealed record Rule(ShipmentDirection Direction, ShipmentStatus From, ShipmentStatus To, bool StaffOverridable);

    private static readonly Rule[] Rules =
    {
        // Outbound
        new(ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier, ShipmentStatus.InInternationalTransit, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.DeliveredAbroad, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.CustomsHold, ShipmentStatus.InInternationalTransit, true),
        new(ShipmentDirection.Outbound, ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException, true),
        new(ShipmentDirection.Outbound, ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit, true),
        new(ShipmentDirection.Outbound, ShipmentStatus.InternationalException, ShipmentStatus.DeliveredAbroad, true),
        // Inbound
        new(ShipmentDirection.Inbound, ShipmentStatus.Created, ShipmentStatus.InInternationalTransit, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, ShipmentStatus.CustomsHold, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.CustomsHold, ShipmentStatus.CustomsClearance, true),
        new(ShipmentDirection.Inbound, ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException, true),
        new(ShipmentDirection.Inbound, ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit, true),
        new(ShipmentDirection.Inbound, ShipmentStatus.InternationalException, ShipmentStatus.CustomsClearance, true),
    };

    // Carriers may make every listed move; staff overrides only the exits from CustomsHold and
    // InternationalException (the rows marked StaffOverridable).
    public static bool IsAllowed(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to, InternationalTransitionTrigger trigger) =>
        Rules.Any(r => r.Direction == direction && r.From == from && r.To == to
            && (trigger == InternationalTransitionTrigger.Carrier || r.StaffOverridable));

    public static ShipmentEventType EventTypeFor(ShipmentStatus to) => to switch
    {
        ShipmentStatus.HandedToCarrier => ShipmentEventType.HandedToCarrier,
        ShipmentStatus.InInternationalTransit => ShipmentEventType.InInternationalTransit,
        ShipmentStatus.DeliveredAbroad => ShipmentEventType.DeliveredAbroad,
        ShipmentStatus.CustomsHold => ShipmentEventType.CustomsHold,
        ShipmentStatus.InternationalException => ShipmentEventType.InternationalException,
        ShipmentStatus.CustomsClearance => ShipmentEventType.CustomsClearance,
        ShipmentStatus.ReceivedFromCarrier => ShipmentEventType.ReceivedFromCarrier,
        _ => throw new ArgumentOutOfRangeException(nameof(to), to, "Not an international carrier-lifecycle status.")
    };
}
