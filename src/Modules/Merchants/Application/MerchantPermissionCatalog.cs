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
    // StaffManage/WebhooksManage — Owner-only. An API key can never hold this scope itself: the
    // key-management endpoints are JWT-only, and ApiKeyService.CreateApiKeyAsync additionally
    // refuses to ever issue a key carrying this scope. That pair is what keeps an API key from
    // managing/revoking other API keys.
    public const string ApiKeysManage = "merchant.apikeys.manage";
    public const string PickupsCreate = "merchant.pickups.create";
    public const string PickupsRead = "merchant.pickups.read";
    public const string PickupsCancel = "merchant.pickups.cancel";
    // Merchant Staff already sees COD amounts embedded in their own shipments via GET
    // /shipments — a settlements read for the same merchant is no new sensitive exposure, so
    // this sits at the same Owner+Staff tier as ShipmentRead, not the Owner-only management tier.
    public const string CodSettlementsRead = "merchant.cod.read";
    // Both merchant roles book shipments and need the same live price-quote preview the staff
    // ShipmentCreateSheet already has (Frontend Phase F10) — same Owner+Staff tier as
    // ShipmentRead/CodSettlementsRead, not an Owner-only management permission.
    public const string PricingRead = "merchant.pricing.read";

    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, WebhooksManage, ApiKeysManage, PickupsCreate, PickupsRead, PickupsCancel, CodSettlementsRead, PricingRead
    };

    private static readonly string[] StaffPermissions =
    {
        ProfileRead, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, PickupsCreate, PickupsRead, PickupsCancel, CodSettlementsRead, PricingRead
    };

    public static readonly IReadOnlyDictionary<MerchantUserRole, IReadOnlySet<string>> RolePermissions =
        new Dictionary<MerchantUserRole, IReadOnlySet<string>>
        {
            [MerchantUserRole.Owner] = OwnerPermissions.ToHashSet(),
            [MerchantUserRole.Staff] = StaffPermissions.ToHashSet()
        };
}
