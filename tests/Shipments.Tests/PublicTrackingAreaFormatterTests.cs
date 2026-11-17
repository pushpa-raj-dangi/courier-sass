using Courier.Shipments.Application;
using Xunit;

namespace Courier.Shipments.Tests;

public class PublicTrackingAreaFormatterTests
{
    [Theory]
    [InlineData("Lalitpur Metropolitan City", "Lalitpur", null, "Lalitpur Metropolitan City, Lalitpur")]
    [InlineData(null, "Lalitpur", null, "Lalitpur")]
    [InlineData(null, null, "Sydney", "Sydney")]
    [InlineData("X", "Y", "Sydney", "X, Y")]
    [InlineData(null, null, null, null)]
    [InlineData(null, null, "  ", null)]
    [InlineData("X", null, null, "X")]
    public void Format_ComposesCoarseAreaOnly(string? localLevel, string? district, string? city, string? expected)
    {
        Assert.Equal(expected, PublicTrackingAreaFormatter.Format(localLevel, district, city));
    }
}
