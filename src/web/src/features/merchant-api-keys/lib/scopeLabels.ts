// Every MerchantPermissionCatalog constant except ApiKeysManage — an issued key can never carry
// that scope itself (server-side refuses it unconditionally, ApiKeyService.CreateApiKeyAsync's
// own doc comment), so it's excluded from the picker rather than offered and silently dropped.
export const AVAILABLE_SCOPES: readonly string[] = [
  "merchant.profile.read",
  "merchant.profile.update",
  "merchant.staff.manage",
  "merchant.kyc.upload",
  "shipment.create",
  "shipment.read",
  "shipment.cancel",
  "merchant.webhooks.manage",
  "merchant.pickups.create",
  "merchant.pickups.read",
  "merchant.pickups.cancel",
  "merchant.cod.read",
  "merchant.pricing.read",
  "merchant.branches.read",
  "merchant.geography.read",
];

export const SCOPE_LABELS: Record<string, string> = {
  "merchant.profile.read": "View business profile",
  "merchant.profile.update": "Edit business profile",
  "merchant.staff.manage": "Manage staff",
  "merchant.kyc.upload": "Upload KYC documents",
  "shipment.create": "Book shipments",
  "shipment.read": "Read shipments",
  "shipment.cancel": "Cancel shipments",
  "merchant.webhooks.manage": "Manage webhooks",
  "merchant.pickups.create": "Request pickups",
  "merchant.pickups.read": "View pickups",
  "merchant.pickups.cancel": "Cancel pickups",
  "merchant.cod.read": "View COD",
  "merchant.pricing.read": "View pricing",
  "merchant.branches.read": "View branches",
  "merchant.geography.read": "View delivery areas",
};

export function scopeLabel(scope: string): string {
  return SCOPE_LABELS[scope] ?? scope;
}
