namespace Courier.Geography.Domain;

public class DeliveryZone
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }

    // Coverage as a navigation (skip-navigation many-to-many via DeliveryZoneAreas), mirroring
    // Role.Permissions — DOMAIN-MODEL.md's "CoveredAreaIds[]" is the DTO-level projection of
    // this, not a raw scalar array column.
    public ICollection<Area> CoveredAreas { get; set; } = new List<Area>();
}
