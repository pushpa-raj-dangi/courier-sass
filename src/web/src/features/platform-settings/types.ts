export type PlatformSettingCategory = "PaymentGateway" | "Notifications" | "Security" | "Branding";
export type PlatformSettingValueType = "String" | "Int" | "Bool";

export interface PlatformSetting {
  key: string;
  category: PlatformSettingCategory;
  valueType: PlatformSettingValueType;
  isSecret: boolean;
  requiresConfirmation: boolean;
  description: string | null;
  isOverridden: boolean;
  displayValue: string | null;
  updatedAt: string | null;
  updatedByUserId: string | null;
}
