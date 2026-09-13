export interface OrganizationProfile {
  supportEmail: string | null;
  supportPhone: string | null;
  addressLine: string | null;
  logoUrl: string | null;
  timeZoneId: string;
  businessHours: string | null;
}

export interface SecurityPolicy {
  refreshTokenLifetimeDays: number;
  minPasswordLength: number;
}

export interface OperationalDefaults {
  defaultServiceTypeId: string | null;
  defaultOriginBranchId: string | null;
}

export interface OrganizationSettings {
  profile: OrganizationProfile;
  security: SecurityPolicy;
  defaults: OperationalDefaults;
}
