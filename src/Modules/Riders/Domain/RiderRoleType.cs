using System.Text.Json.Serialization;

namespace Courier.Riders.Domain;

// Maps 1:1 onto the Identity.Role names seeded by AuthService.RegisterOrganizationAsync
// ("PickupRider"/"DeliveryRider", Task 3) — RiderService.CreateRiderAsync converts this enum to
// the matching role name string when calling IUserProvisioningService.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RiderRoleType
{
    PickupRider,
    DeliveryRider
}
