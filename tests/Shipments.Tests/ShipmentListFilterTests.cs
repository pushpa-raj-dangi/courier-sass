using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class ShipmentListFilterTests
{
    [Fact]
    public void Parse_KnownStatuses_ReturnsThem()
    {
        var filter = ShipmentListFilter.Parse(new[] { "InTransit", "Sorted" }, null, null, null, null, null, null);
        Assert.Equal(new[] { ShipmentStatus.InTransit, ShipmentStatus.Sorted }, filter.Statuses);
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("3")]
    [InlineData("intransit")]
    public void Parse_UnknownOrNumericOrWrongCaseStatus_Throws(string raw)
    {
        Assert.Throws<ShipmentValidationException>(() => ShipmentListFilter.Parse(new[] { raw }, null, null, null, null, null, null));
    }

    [Fact]
    public void Parse_OneCharQuery_Throws()
    {
        Assert.Throws<ShipmentValidationException>(() => ShipmentListFilter.Parse(null, " a ", null, null, null, null, null));
    }

    [Fact]
    public void Parse_WhitespaceQuery_IsIgnored()
    {
        var filter = ShipmentListFilter.Parse(null, "   ", null, null, null, null, null);
        Assert.Null(filter.Query);
    }

    [Fact]
    public void Parse_QueryIsTrimmed()
    {
        var filter = ShipmentListFilter.Parse(null, "  sita ", null, null, null, null, null);
        Assert.Equal("sita", filter.Query);
    }

    [Fact]
    public void Parse_FromAfterTo_Throws()
    {
        var to = DateTimeOffset.UtcNow;
        Assert.Throws<ShipmentValidationException>(() => ShipmentListFilter.Parse(null, null, null, null, null, to.AddDays(1), to));
    }

    [Fact]
    public void Parse_NonUtcOffsets_AreNormalisedToUtcSameInstant()
    {
        var from = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromMinutes(345));
        var to = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromMinutes(345));
        var filter = ShipmentListFilter.Parse(null, null, null, null, null, from, to);
        Assert.Equal(TimeSpan.Zero, filter.CreatedFrom!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, filter.CreatedTo!.Value.Offset);
        Assert.Equal(from.UtcDateTime, filter.CreatedFrom.Value.UtcDateTime);
        Assert.Equal(to.UtcDateTime, filter.CreatedTo.Value.UtcDateTime);
    }

    [Fact]
    public void Parse_NoInput_IsEmpty()
    {
        var filter = ShipmentListFilter.Parse(null, null, null, null, null, null, null);
        Assert.True(filter.IsEmpty);
    }
}
