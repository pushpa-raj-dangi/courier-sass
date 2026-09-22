namespace Courier.Geography.Domain;

// Platform-level (no OrganizationId) grouping of countries for future pricing/reporting (I5).
// Ships with zero rows — a SuperAdmin defines zones and assigns countries via the admin API.
public class CountryZone
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
