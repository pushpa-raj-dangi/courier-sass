using System.Threading;
using System.Threading.Tasks;
using Courier.Geography.Application;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class ShipmentAddressValidatorTests
{
    private static Shipment DomesticShipment() => new()
    {
        Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Awb = "AWB1", MerchantId = Guid.NewGuid(),
        OriginBranchId = Guid.NewGuid(), DestinationBranchId = Guid.NewGuid(), CurrentStatus = ShipmentStatus.Created,
        ServiceTypeId = Guid.NewGuid(), WeightKg = 1m,
        Scope = ShipmentScope.Domestic, Direction = ShipmentDirection.Domestic,
        RecipientName = "R", RecipientPhone = "1", RecipientProvinceId = Guid.NewGuid(), RecipientDistrictId = Guid.NewGuid(),
        RecipientLocalLevelId = Guid.NewGuid(), RecipientWardNumber = 1, RecipientAddressLine = "Line",
        SenderName = "S", SenderPhone = "2", SenderProvinceId = Guid.NewGuid(), SenderDistrictId = Guid.NewGuid(),
        SenderLocalLevelId = Guid.NewGuid(), SenderWardNumber = 1, SenderAddressLine = "Line"
    };

    private static Shipment OutboundShipment(bool withForeignRecipient) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Awb = "AWB2", MerchantId = Guid.NewGuid(),
        OriginBranchId = Guid.NewGuid(), DestinationBranchId = Guid.NewGuid(), CurrentStatus = ShipmentStatus.Created,
        ServiceTypeId = Guid.NewGuid(), WeightKg = 1m,
        Scope = ShipmentScope.International, Direction = ShipmentDirection.Outbound,
        RecipientName = "R", RecipientPhone = "1",
        RecipientProvinceId = withForeignRecipient ? null : Guid.NewGuid(),
        RecipientDistrictId = withForeignRecipient ? null : Guid.NewGuid(),
        RecipientLocalLevelId = withForeignRecipient ? null : Guid.NewGuid(),
        RecipientWardNumber = withForeignRecipient ? null : 1,
        RecipientAddressLine = "Foreign Line1",
        RecipientCity = withForeignRecipient ? "Mumbai" : null,
        RecipientCountryCode = withForeignRecipient ? "IN" : null,
        SenderName = "S", SenderPhone = "2", SenderProvinceId = Guid.NewGuid(), SenderDistrictId = Guid.NewGuid(),
        SenderLocalLevelId = Guid.NewGuid(), SenderWardNumber = 1, SenderAddressLine = "Line"
    };

    [Fact]
    public async Task ValidateAsync_DomesticShipment_WithFullNepalChains_Succeeds()
    {
        await ShipmentAddressValidator.ValidateAsync(DomesticShipment(), new AlwaysTrueGeographyLookup(), CancellationToken.None);
    }

    [Fact]
    public async Task ValidateAsync_OutboundShipment_WithForeignRecipient_Succeeds()
    {
        await ShipmentAddressValidator.ValidateAsync(OutboundShipment(withForeignRecipient: true), new AlwaysTrueGeographyLookup(), CancellationToken.None);
    }

    [Fact]
    public async Task ValidateAsync_OutboundShipment_WithNepalRecipientInstead_Throws()
    {
        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            ShipmentAddressValidator.ValidateAsync(OutboundShipment(withForeignRecipient: false), new AlwaysTrueGeographyLookup(), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_OutboundShipment_WithNepalSenderMissing_Throws()
    {
        var shipment = OutboundShipment(withForeignRecipient: true);
        shipment.SenderProvinceId = null;
        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            ShipmentAddressValidator.ValidateAsync(shipment, new AlwaysTrueGeographyLookup(), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_OutboundShipment_WithInvalidCountryCode_Throws()
    {
        var shipment = OutboundShipment(withForeignRecipient: true);
        shipment.RecipientCountryCode = "X";
        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            ShipmentAddressValidator.ValidateAsync(shipment, new AlwaysTrueGeographyLookup(), CancellationToken.None));
    }
}
