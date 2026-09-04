using System.Text.Json;
using Courier.Shipments.Application;
using Xunit;

namespace Courier.Shipments.Tests;

public class PublicTrackingStatusSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(PublicTrackingStatus.InTransit);

        Assert.Equal("\"InTransit\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<PublicTrackingStatus>("\"Delivered\"");

        Assert.Equal(PublicTrackingStatus.Delivered, value);
    }
}
