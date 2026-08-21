namespace Courier.Identity.Application;

public record ProvisionStaffUserRequest(string Email, string Password, string FullName, string? Phone, string RoleName, Guid BranchId);

// Creates a new staff User + role/branch grant without letting a calling module (Riders, Task 8)
// touch Identity's DbSet<User>/DbSet<Role>/DbSet<UserRole>/DbSet<UserBranch> directly — the same
// narrow-interface discipline as IBranchLookupService (Phase 5) and IShipmentTransitionService
// (Phase 7). Deliberately NOT a general "create any user with any role" endpoint (that remains
// deferred, per this plan's YAGNI scoping) — RoleName must already be a role seeded for this
// organization (RegisterOrganizationAsync seeds OrganizationOwner, PickupRider, DeliveryRider).
public interface IUserProvisioningService
{
    Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default);
}

public class UserProvisioningException : Exception
{
    public UserProvisioningException(string message) : base(message) { }
}
