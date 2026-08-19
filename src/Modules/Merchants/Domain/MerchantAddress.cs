namespace Courier.Merchants.Domain;

public class MerchantAddress
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }

    // Denormalized from Merchant.OrganizationId at creation time, never updated after — lets
    // this entity carry its own tenant query filter directly instead of requiring every query
    // to join through Merchant first. Mirrors CLARIFICATIONS.md #7's identical reasoning for
    // ApiKey's denormalized OrganizationId.
    public Guid OrganizationId { get; set; }

    public required string Label { get; set; }
    public required Guid ProvinceId { get; set; }
    public required Guid DistrictId { get; set; }
    public required Guid LocalLevelId { get; set; }
    public required int WardNumber { get; set; }
    public required string AddressLine { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsDefaultPickup { get; set; }
}
