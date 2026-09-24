using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class InternationalTransitionRulesTests
{
    [Theory]
    [InlineData(ShipmentStatus.HandedToCarrier, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.DeliveredAbroad)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.DeliveredAbroad)]
    public void Outbound_CarrierTransitions_AreAllowed(ShipmentStatus from, ShipmentStatus to)
    {
        Assert.True(InternationalTransitionRules.IsAllowed(ShipmentDirection.Outbound, from, to, InternationalTransitionTrigger.Carrier));
    }

    [Theory]
    [InlineData(ShipmentStatus.Created, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.CustomsClearance, ShipmentStatus.CustomsHold)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.CustomsClearance)]
    public void Inbound_CarrierTransitions_AreAllowed(ShipmentStatus from, ShipmentStatus to)
    {
        Assert.True(InternationalTransitionRules.IsAllowed(ShipmentDirection.Inbound, from, to, InternationalTransitionTrigger.Carrier));
    }

    [Theory]
    // Backwards / skipping / wrong-direction moves the carrier must never make.
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.DeliveredAbroad, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier, ShipmentStatus.DeliveredAbroad)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.CustomsPrepared, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.DeliveredAbroad)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.Created, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, ShipmentStatus.ReceivedFromCarrier)]
    [InlineData(ShipmentDirection.Domestic, ShipmentStatus.Created, ShipmentStatus.InInternationalTransit)]
    public void DisallowedCarrierTransitions_AreRejected(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to)
    {
        Assert.False(InternationalTransitionRules.IsAllowed(direction, from, to, InternationalTransitionTrigger.Carrier));
    }

    [Theory]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.CustomsHold, ShipmentStatus.InInternationalTransit, true)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.InternationalException, ShipmentStatus.DeliveredAbroad, true)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.CustomsHold, ShipmentStatus.CustomsClearance, true)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit, true)]
    // Overrides are only exits from the two exception statuses.
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold, false)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier, ShipmentStatus.InInternationalTransit, false)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.Created, ShipmentStatus.InInternationalTransit, false)]
    public void StaffOverride_OnlyExitsExceptionStatuses(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to, bool expected)
    {
        Assert.Equal(expected, InternationalTransitionRules.IsAllowed(direction, from, to, InternationalTransitionTrigger.StaffOverride));
    }

    [Fact]
    public void EventTypeFor_MapsEachCarrierStatusToItsSameNamedEventType()
    {
        Assert.Equal(ShipmentEventType.CustomsHold, InternationalTransitionRules.EventTypeFor(ShipmentStatus.CustomsHold));
        Assert.Equal(ShipmentEventType.DeliveredAbroad, InternationalTransitionRules.EventTypeFor(ShipmentStatus.DeliveredAbroad));
        Assert.Throws<ArgumentOutOfRangeException>(() => InternationalTransitionRules.EventTypeFor(ShipmentStatus.Delivered));
    }
}
