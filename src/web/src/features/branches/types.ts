export type BranchType = "HeadOffice" | "Hub" | "Branch" | "PickupPoint" | "Warehouse";

export interface Branch {
  id: string;
  code: string;
  name: string;
  type: BranchType;
  provinceId: string;
  districtId: string;
  localLevelId: string;
  wardNumber: number;
  addressLine: string;
  phone: string | null;
  operatingHours: string | null;
  latitude: number | null;
  longitude: number | null;
  parentBranchId: string | null;
  deliveryZoneId: string | null;
  isActive: boolean;
}

export interface CreateBranchInput {
  code: string;
  name: string;
  type: BranchType;
  provinceId: string;
  districtId: string;
  localLevelId: string;
  wardNumber: number;
  addressLine: string;
  phone?: string;
  operatingHours?: string;
  parentBranchId?: string;
}
