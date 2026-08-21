namespace Courier.Manifests.Domain;

public class Manifest
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public string? VehiclePlate { get; set; }
    public Guid? DriverId { get; set; }
    public ManifestStatus Status { get; set; }
    public int ShipmentCount { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
