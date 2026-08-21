namespace Courier.Riders.Domain;

// UserId links to a real Identity.User holding the PickupRider/DeliveryRider role (DOMAIN-MODEL.md:
// "a rider is also a User with the PickupRider/DeliveryRider role") — created together with this
// row by RiderService.CreateRiderAsync via Identity.Application.IUserProvisioningService, never
// referencing Identity's DbSet directly (Riders has no project reference to Identity's
// Infrastructure/Persistence, only its Application-layer interface).
// No RiderAssignment entity this phase (this plan's resolved design decision) — BaseBranchId is
// the sole branch scope; a real re-assignment feature is deferred until a concrete need appears.
public class Rider
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid BaseBranchId { get; set; }
    public required string LicenseNumber { get; set; }
    public RiderRoleType RoleType { get; set; }
    public bool IsActive { get; set; } = true;
}
