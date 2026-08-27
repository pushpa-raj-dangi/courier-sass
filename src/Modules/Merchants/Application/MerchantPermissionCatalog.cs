using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

// Merchant-facing permissions are a small, fixed catalog — unlike staff Roles, merchants cannot
// define custom roles in v1 (RBAC.md §5). Owner holds every merchant permission; Staff holds a
// fixed subset with no staff-management visibility.
public static class MerchantPermissionCatalog
{
    public const string ProfileRead = "merchant.profile.read";
    public const string ProfileUpdate = "merchant.profile.update";
    public const string StaffManage = "merchant.staff.manage";
    public const string KycUpload = "merchant.kyc.upload";
    // Same string values as the staff PermissionCatalog's shipment.create/read/cancel —
    // two independent catalogs/checkers (MerchantAuthorizationChecker vs PermissionChecker),
    // same key names by design (ROADMAP.md Phase 6).
    public const string ShipmentCreate = "shipment.create";
    public const string ShipmentRead = "shipment.read";
    public const string ShipmentCancel = "shipment.cancel";
    // Webhook subscription management is an account-level integration setting, same sensitivity
    // tier as StaffManage/ProfileUpdate — Owner-only, not shared with Staff (Phase 11).
    public const string WebhooksManage = "merchant.webhooks.manage";
    // API key management is an account-level integration setting, same sensitivity tier as
    // StaffManage/WebhooksManage — Owner-only. An API key can never hold this scope itself
    // (enforced by keeping key-management endpoints JWT-only in Task 4, not by this catalog),
    // which is what keeps an API key from managing/revoking other API keys.
    public const string ApiKeysManage = "merchant.apikeys.manage";

    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, WebhooksManage, ApiKeysManage
    };

    private static readonly string[] StaffPermissions =
    {
        ProfileRead, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel
    };

    public static readonly IReadOnlyDictionary<MerchantUserRole, IReadOnlySet<string>> RolePermissions =
        new Dictionary<MerchantUserRole, IReadOnlySet<string>>
        {
            [MerchantUserRole.Owner] = OwnerPermissions.ToHashSet(),
            [MerchantUserRole.Staff] = StaffPermissions.ToHashSet()
        };
}
