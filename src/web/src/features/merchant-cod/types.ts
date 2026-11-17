export type { MerchantSettlement, MerchantSettlementItem, MerchantSettlementStatus } from "@/features/cod/types";

export type MoneyBucket = { amount: number; count: number };

export type SettledBucket = { amount: number; payouts: number; lastSettledAt: string | null };

export type DeliveredTodayBucket = { parcels: number; codCollected: number };

export type MerchantCodSummary = {
  collectedNotSettled: MoneyBucket;
  inSettlement: MoneyBucket;
  settledThisMonth: SettledBucket;
  deliveredToday: DeliveredTodayBucket;
};

export type MerchantAwaitingCollection = {
  shipmentId: string;
  awb: string;
  collectedAt: string;
  collectedAmount: number;
  courierFee: number;
  net: number;
};

export type MerchantCodState =
  | "Prepaid"
  | "NotCollected"
  | "CollectedAwaitingSettlement"
  | "InSettlement"
  | "Settled";

export type MerchantShipmentCodStatus = {
  state: MerchantCodState;
  settlementId: string | null;
  periodFrom: string | null;
  periodTo: string | null;
  settledAt: string | null;
};
