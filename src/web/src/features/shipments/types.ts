export interface ShipmentItem {
  id: string;
  description: string;
  quantity: number;
  unitValue: number;
}

export interface Shipment {
  id: string;
  awb: string;
  merchantId: string;
  originBranchId: string;
  destinationBranchId: string;
  currentBranchId: string | null;
  currentCustodianUserId: string | null;
  currentCustodianRiderId: string | null;
  currentStatus: string;
  serviceTypeId: string;
  weightKg: number;
  lengthCm: number | null;
  widthCm: number | null;
  heightCm: number | null;
  declaredValue: number;
  codAmount: number;
  wantsInsurance: boolean;
  deliveryCharge: number;
  insuranceCharge: number;
  otherCharges: number;
  totalCharge: number;
  recipientName: string;
  recipientPhone: string;
  recipientAddressLine: string;
  senderName: string;
  senderPhone: string;
  senderAddressLine: string;
  items: ShipmentItem[];
  createdAt: string;
}

export interface CreateShipmentItemInput {
  description: string;
  quantity: number;
  unitValue: number;
}

export interface CreateShipmentInput {
  merchantId: string;
  originBranchId: string;
  destinationBranchId: string;
  serviceTypeId: string;
  weightKg: number;
  lengthCm?: number;
  widthCm?: number;
  heightCm?: number;
  codAmount: number;
  wantsInsurance: boolean;
  recipientName: string;
  recipientPhone: string;
  recipientProvinceId: string;
  recipientDistrictId: string;
  recipientLocalLevelId: string;
  recipientWardNumber: number;
  recipientAddressLine: string;
  senderName: string;
  senderPhone: string;
  senderProvinceId: string;
  senderDistrictId: string;
  senderLocalLevelId: string;
  senderWardNumber: number;
  senderAddressLine: string;
  items: CreateShipmentItemInput[];
}

export interface ServiceType {
  id: string;
  // `code` deliberately omitted — ServiceTypeResponse.Code has no JsonStringEnumConverter and
  // serializes as a raw integer over the wire (same latent bug BranchType had before its
  // Phase F2 fix). Only `id`/`name` are safe to consume; never render `.code`.
  name: string;
}

export interface Merchant {
  id: string;
  displayName: string;
  isActive: boolean;
}
