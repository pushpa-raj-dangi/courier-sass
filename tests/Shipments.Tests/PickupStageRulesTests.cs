using Courier.Shipments.Domain;

namespace Courier.Shipments.Tests;

public class PickupStageRulesTests
{
    [Theory]
    [InlineData(new ShipmentStatus[0])]
    [InlineData(new[] { ShipmentStatus.Delivered, ShipmentStatus.PickedUp })]
    [InlineData(new[] { ShipmentStatus.Created })]
    public void StoredCancelled_IsCancelled_RegardlessOfParcels(ShipmentStatus[] parcels)
    {
        var stage = PickupStageRules.Derive(PickupRequestStatus.Cancelled, parcels);

        Assert.Equal(PickupStage.Cancelled, stage);
    }

    [Theory]
    [InlineData(new ShipmentStatus[0])]
    [InlineData(new[] { ShipmentStatus.Created, ShipmentStatus.PickupAssigned })]
    [InlineData(new[] { ShipmentStatus.Delivered })]
    public void StoredCompleted_IsCollected_RegardlessOfParcels(ShipmentStatus[] parcels)
    {
        var stage = PickupStageRules.Derive(PickupRequestStatus.Completed, parcels);

        Assert.Equal(PickupStage.Collected, stage);
    }

    [Theory]
    [MemberData(nameof(RequestedOrScheduledCases))]
    public void StoredRequestedOrScheduled_DerivesStageFromParcels(
        PickupRequestStatus stored,
        ShipmentStatus[] parcels,
        PickupStage expected)
    {
        var stage = PickupStageRules.Derive(stored, parcels);

        Assert.Equal(expected, stage);
    }

    public static IEnumerable<object[]> RequestedOrScheduledCases()
    {
        foreach (var stored in new[] { PickupRequestStatus.Requested, PickupRequestStatus.Scheduled })
        {
            yield return new object[] { stored, new ShipmentStatus[0], PickupStage.Requested };
            yield return new object[] { stored, new[] { ShipmentStatus.Cancelled, ShipmentStatus.Cancelled }, PickupStage.Requested };
            yield return new object[] { stored, new[] { ShipmentStatus.Created, ShipmentStatus.Created }, PickupStage.Requested };
            yield return new object[] { stored, new[] { ShipmentStatus.Created, ShipmentStatus.PickupAssigned }, PickupStage.Scheduled };
            yield return new object[] { stored, new[] { ShipmentStatus.PickupAssigned }, PickupStage.Scheduled };
            yield return new object[] { stored, new[] { ShipmentStatus.Created, ShipmentStatus.PickedUp }, PickupStage.Scheduled };
            yield return new object[] { stored, new[] { ShipmentStatus.PickedUp, ShipmentStatus.Delivered }, PickupStage.Collected };
            yield return new object[] { stored, new[] { ShipmentStatus.PickedUp, ShipmentStatus.Cancelled }, PickupStage.Collected };
            yield return new object[] { stored, new[] { ShipmentStatus.RtoInitiated }, PickupStage.Collected };
            yield return new object[] { stored, new[] { ShipmentStatus.InInternationalTransit }, PickupStage.Collected };
        }
    }

    [Fact]
    public void IsPastPickup_IsFalseOnlyForCreatedPickupAssignedAndCancelled()
    {
        var notPastPickup = new HashSet<ShipmentStatus>
        {
            ShipmentStatus.Created,
            ShipmentStatus.PickupAssigned,
            ShipmentStatus.Cancelled,
        };

        foreach (var status in Enum.GetValues<ShipmentStatus>())
        {
            Assert.Equal(!notPastPickup.Contains(status), PickupStageRules.IsPastPickup(status));
        }
    }
}
