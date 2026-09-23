using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

// Enforces the I3 design spec §3 shape rule: domestic shipments (and the domestic-side party of
// an international shipment) require a full, valid Nepal geography chain; the foreign-side party
// (determined by Direction — Outbound means the recipient is foreign, Inbound means the sender
// is) requires InternationalAddress-shaped fields instead. Called by
// ShipmentService.CreateShipmentAsync (domestic, both parties are Nepal) and
// ShipmentService.CreateShipmentShellAsync (I3, Task 6).
public static class ShipmentAddressValidator
{
    public static async Task ValidateAsync(Shipment shipment, IGeographyLookupService geographyLookupService, CancellationToken cancellationToken)
    {
        if (shipment.Scope == ShipmentScope.Domestic)
        {
            await ValidateNepalPartyAsync("Recipient", shipment.RecipientProvinceId, shipment.RecipientDistrictId, shipment.RecipientLocalLevelId, shipment.RecipientWardNumber, geographyLookupService, cancellationToken);
            await ValidateNepalPartyAsync("Sender", shipment.SenderProvinceId, shipment.SenderDistrictId, shipment.SenderLocalLevelId, shipment.SenderWardNumber, geographyLookupService, cancellationToken);
            return;
        }

        var recipientIsForeign = shipment.Direction == ShipmentDirection.Outbound;
        if (recipientIsForeign)
        {
            await ValidateNepalPartyAsync("Sender", shipment.SenderProvinceId, shipment.SenderDistrictId, shipment.SenderLocalLevelId, shipment.SenderWardNumber, geographyLookupService, cancellationToken);
            ValidateForeignParty("Recipient", shipment.RecipientAddressLine, shipment.RecipientCity, shipment.RecipientCountryCode);
        }
        else
        {
            await ValidateNepalPartyAsync("Recipient", shipment.RecipientProvinceId, shipment.RecipientDistrictId, shipment.RecipientLocalLevelId, shipment.RecipientWardNumber, geographyLookupService, cancellationToken);
            ValidateForeignParty("Sender", shipment.SenderAddressLine, shipment.SenderCity, shipment.SenderCountryCode);
        }
    }

    private static async Task ValidateNepalPartyAsync(string party, Guid? provinceId, Guid? districtId, Guid? localLevelId, int? wardNumber, IGeographyLookupService geographyLookupService, CancellationToken cancellationToken)
    {
        if (provinceId is not Guid p || districtId is not Guid d || localLevelId is not Guid l || wardNumber is not int w)
        {
            throw new ShipmentValidationException($"{party} must have a full Nepal province/district/local level/ward.");
        }
        if (!await geographyLookupService.IsValidHierarchyAsync(p, d, l, w, cancellationToken))
        {
            throw new ShipmentValidationException($"{party} province/district/local level/ward do not form a valid geography chain.");
        }
    }

    private static void ValidateForeignParty(string party, string line1, string? city, string? countryCode)
    {
        try
        {
            InternationalAddress.Create(line1, null, city ?? string.Empty, null, null, countryCode ?? string.Empty);
        }
        catch (InternationalAddressValidationException ex)
        {
            throw new ShipmentValidationException($"{party} international address is invalid: {ex.Message}");
        }
    }
}
