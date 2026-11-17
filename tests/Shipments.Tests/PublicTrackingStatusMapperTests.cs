using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class PublicTrackingStatusMapperTests
{
    [Theory]
    [InlineData(ShipmentStatus.Created, PublicTrackingStatus.Received)]
    [InlineData(ShipmentStatus.PickupAssigned, PublicTrackingStatus.Received)]
    [InlineData(ShipmentStatus.PickedUp, PublicTrackingStatus.PickedUp)]
    [InlineData(ShipmentStatus.AtOriginBranch, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.Sorted, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.InTransit, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.AtDestinationBranch, PublicTrackingStatus.AtDestinationBranch)]
    [InlineData(ShipmentStatus.OutForDelivery, PublicTrackingStatus.OutForDelivery)]
    [InlineData(ShipmentStatus.Delivered, PublicTrackingStatus.Delivered)]
    [InlineData(ShipmentStatus.DeliveryFailed, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.NdrCreated, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.RtoInitiated, PublicTrackingStatus.ReturningToSender)]
    [InlineData(ShipmentStatus.RtoInTransit, PublicTrackingStatus.ReturningToSender)]
    [InlineData(ShipmentStatus.RtoAtOrigin, PublicTrackingStatus.ReturningToSender)]
    [InlineData(ShipmentStatus.RtoDelivered, PublicTrackingStatus.ReturningToSender)]
    [InlineData(ShipmentStatus.Cancelled, PublicTrackingStatus.Cancelled)]
    public void Map_KnownStatus_ReturnsExpectedPublicStatus(ShipmentStatus input, PublicTrackingStatus expected)
    {
        Assert.Equal(expected, PublicTrackingStatusMapper.Map(input));
    }

    [Theory]
    [InlineData(ShipmentStatus.HandedToCarrier, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.InInternationalTransit, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.CustomsClearance, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.ReceivedFromCarrier, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.CustomsHold, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.InternationalException, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.DeliveredAbroad, PublicTrackingStatus.Delivered)]
    public void Map_InternationalCarrierStatuses_HidesExceptionsBehindInTransit(ShipmentStatus status, PublicTrackingStatus expected)
    {
        Assert.Equal(expected, PublicTrackingStatusMapper.Map(status));
    }

    [Fact]
    public void Map_EveryDefinedShipmentStatus_HasAMapping()
    {
        // Guards against a future ShipmentStatus value silently leaking through unmapped —
        // exactly the property spec §12's testing section requires.
        foreach (var status in Enum.GetValues<ShipmentStatus>())
        {
            var exception = Record.Exception(() => PublicTrackingStatusMapper.Map(status));
            Assert.Null(exception);
        }
    }
}
