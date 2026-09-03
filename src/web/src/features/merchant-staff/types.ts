export interface MerchantStaffMember {
  id: string;
  email: string;
  fullName: string;
  role: "Owner" | "Staff";
  isActive: boolean;
}
