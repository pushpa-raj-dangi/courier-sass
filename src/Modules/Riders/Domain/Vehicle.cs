namespace Courier.Riders.Domain;

// No dedicated vehicle.* permission (this plan's resolved design decision) — Vehicle is fleet
// metadata owned by the same rider.create/rider.read permissions Rider itself uses; a full fleet
// module with its own permission surface is deferred until a concrete need appears.
public class Vehicle
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? RiderId { get; set; }
    public required string Type { get; set; }
    public required string PlateNumber { get; set; }
    public decimal? Capacity { get; set; }
}
