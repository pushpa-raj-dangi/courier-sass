using System.Text.Json;
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class PickupRequestStatusSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(PickupRequestStatus.Scheduled);

        Assert.Equal("\"Scheduled\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<PickupRequestStatus>("\"Completed\"");

        Assert.Equal(PickupRequestStatus.Completed, value);
    }
}
