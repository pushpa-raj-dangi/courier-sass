export type { Shipment, ShipmentItem, ServiceType, QuoteResponse } from "@/features/shipments/types";
export type { Branch } from "@/features/branches/types";

export interface CreateMerchantShipmentItemInput {
  description: string;
  quantity: number;
  unitValue: number;
}
