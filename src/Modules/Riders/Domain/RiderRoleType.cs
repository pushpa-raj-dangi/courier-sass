namespace Courier.Riders.Domain;

// Maps 1:1 onto the Identity.Role names seeded by AuthService.RegisterOrganizationAsync
// ("PickupRider"/"DeliveryRider", Task 3) — RiderService.CreateRiderAsync converts this enum to
// the matching role name string when calling IUserProvisioningService.
public enum RiderRoleType
{
    PickupRider,
    DeliveryRider
}
