namespace Courier.Geography.Domain;

// Platform-level reference data (no OrganizationId, no tenant query filter) — same reasoning as
// Pricing's FxRate. The row set is fixed to the seeded ISO-3166-1 alpha-2 list (see
// CountrySeedData); only IsServiced and CountryZoneId are ever mutated after seeding, via
// CountryAdminService. No create/delete API exists for this entity.
public class Country
{
    public Guid Id { get; set; }
    public required string IsoCode { get; set; }
    public required string NameEn { get; set; }
    public string? NameNe { get; set; }
    public required string DefaultCurrency { get; set; }
    public required string CallingCode { get; set; }
    public bool IsServiced { get; set; }
    public Guid? CountryZoneId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
