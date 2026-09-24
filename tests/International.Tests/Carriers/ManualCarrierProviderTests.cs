using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Courier.Shipments.Domain;

namespace Courier.International.Tests.Carriers;

public class ManualCarrierProviderTests
{
    private readonly ManualCarrierProvider _provider = new();

    [Theory]
    [InlineData("IN_TRANSIT", ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit)]
    [InlineData("IN_TRANSIT", ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit)]
    [InlineData("IN_CUSTOMS", ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance)]
    [InlineData("CLEARED", ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit)]
    [InlineData("CLEARED", ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance)]
    [InlineData("CUSTOMS_HOLD", ShipmentDirection.Outbound, ShipmentStatus.CustomsHold)]
    [InlineData("CUSTOMS_HOLD", ShipmentDirection.Inbound, ShipmentStatus.CustomsHold)]
    [InlineData("EXCEPTION", ShipmentDirection.Outbound, ShipmentStatus.InternationalException)]
    [InlineData("EXCEPTION", ShipmentDirection.Inbound, ShipmentStatus.InternationalException)]
    [InlineData("DELIVERED", ShipmentDirection.Outbound, ShipmentStatus.DeliveredAbroad)]
    [InlineData("in_transit", ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit)]
    public void MapStatus_KnownCode_MapsPerDirection(string code, ShipmentDirection direction, ShipmentStatus expected)
    {
        Assert.Equal(expected, _provider.MapStatus(code, direction));
    }

    [Theory]
    [InlineData("IN_CUSTOMS", ShipmentDirection.Outbound)]
    [InlineData("DELIVERED", ShipmentDirection.Inbound)]
    [InlineData("ARRIVED", ShipmentDirection.Outbound)]
    [InlineData("ARRIVED", ShipmentDirection.Inbound)]
    [InlineData("NOT_A_CODE", ShipmentDirection.Outbound)]
    public void MapStatus_InformationalOrUnknownCode_ReturnsNull(string code, ShipmentDirection direction)
    {
        Assert.Null(_provider.MapStatus(code, direction));
    }

    [Theory]
    [InlineData("ARRIVED", true)]
    [InlineData("delivered", true)]
    [InlineData("NOT_A_CODE", false)]
    public void IsKnownStatusCode_ReflectsTheVocabulary(string code, bool expected)
    {
        Assert.Equal(expected, _provider.IsKnownStatusCode(code));
    }

    [Fact]
    public async Task BookAsync_UsesTheStaffSuppliedAwb()
    {
        var result = await _provider.BookAsync(new CarrierBookingRequest(Guid.NewGuid(), "KTM-1", "EXPRESS", "  MAN-778  ", 1m), CarrierCredentials.Empty, "key-1", CancellationToken.None);

        Assert.Equal("MAN-778", result.CarrierAwb);
        Assert.Equal("EXPRESS", result.ServiceLevel);
        Assert.Null(result.Label);
    }

    [Fact]
    public async Task BookAsync_WithoutAwb_ThrowsBookingException()
    {
        await Assert.ThrowsAsync<CarrierBookingException>(() =>
            _provider.BookAsync(new CarrierBookingRequest(Guid.NewGuid(), "KTM-1", null, null, 1m), CarrierCredentials.Empty, "key-1", CancellationToken.None));
    }

    [Fact]
    public void Capabilities_ManualAwbOnly()
    {
        Assert.Equal(new CarrierCapabilities(SupportsWebhooks: false, SupportsPolling: false, RequiresManualAwb: true, SupportsLabels: false), _provider.Capabilities);
    }
}
