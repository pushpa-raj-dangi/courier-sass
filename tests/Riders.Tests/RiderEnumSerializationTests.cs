using System.Text.Json;
using Courier.Riders.Domain;
using Xunit;

namespace Courier.Riders.Tests;

public class RiderEnumSerializationTests
{
    [Fact]
    public void DeliveryTaskType_SerializesAsString() =>
        Assert.Equal("\"Pickup\"", JsonSerializer.Serialize(DeliveryTaskType.Pickup));

    [Fact]
    public void DeliveryTaskStatus_SerializesAsString() =>
        Assert.Equal("\"Pending\"", JsonSerializer.Serialize(DeliveryTaskStatus.Pending));

    [Fact]
    public void DeliveryRunStatus_SerializesAsString() =>
        Assert.Equal("\"Active\"", JsonSerializer.Serialize(DeliveryRunStatus.Active));

    [Fact]
    public void RiderRoleType_SerializesAsString() =>
        Assert.Equal("\"PickupRider\"", JsonSerializer.Serialize(RiderRoleType.PickupRider));

    [Fact]
    public void DeliveryAttemptReason_SerializesAsString() =>
        Assert.Equal("\"CustomerUnavailable\"", JsonSerializer.Serialize(DeliveryAttemptReason.CustomerUnavailable));
}
