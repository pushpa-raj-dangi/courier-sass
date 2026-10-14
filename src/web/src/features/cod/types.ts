export type CodCollectionStatus =
  | "CodExpected" | "CodCollected" | "RiderDeposited" | "BranchReconciled"
  | "FinanceVerified" | "MerchantSettlementCreated" | "Settled";

export interface CodCollection {
  id: string;
  shipmentId: string;
  merchantId: string;
  branchId: string;
  expectedAmount: number;
  shipmentTotalCharge: number;
  collectedAmount: number | null;
  status: CodCollectionStatus;
  collectedByRiderId: string | null;
  collectedAt: string | null;
  deliveryProofId: string | null;
  riderCashSettlementId: string | null;
  merchantSettlementItemId: string | null;
  createdAt: string;
  // Null only when the shipment lookup misses.
  awb: string | null;
}

export interface RiderCashSettlement {
  id: string;
  riderId: string;
  branchId: string;
  declaredAmount: number;
  shipmentCount: number;
  settledAt: string;
  confirmedByUserId: string;
  branchCashSettlementId: string | null;
  expectedAmount: number;
  variance: number;
}

export interface CreateRiderSettlementInput {
  riderId: string;
  branchId: string;
  declaredAmount: number;
}

export interface BranchCashSettlement {
  id: string;
  branchId: string;
  confirmedAmount: number;
  reconciledByUserId: string;
  reconciledAt: string;
  riderCashSettlementIds: string[];
}

export interface CreateBranchSettlementInput {
  branchId: string;
  riderCashSettlementIds: string[];
  confirmedAmount: number;
}

export interface CreateFinanceVerificationInput {
  branchCashSettlementId: string;
  amount: number;
}

export type MerchantSettlementStatus = "Created" | "Settled";

export interface MerchantSettlementItem {
  id: string;
  shipmentId: string;
  shipmentAwb: string;
  codAmount: number;
  courierFee: number;
  otherDeductions: number;
  netPayable: number;
}

export interface MerchantSettlement {
  id: string;
  merchantId: string;
  periodFrom: string;
  periodTo: string;
  totalPayable: number;
  status: MerchantSettlementStatus;
  settledAt: string | null;
  items: MerchantSettlementItem[];
}

export interface CreateMerchantSettlementInput {
  merchantId: string;
  periodFrom: string;
  periodTo: string;
}

export interface Adjustment {
  id: string;
  entityType: string;
  entityId: string;
  beforeValue: number;
  afterValue: number;
  reason: string;
  requestedByUserId: string;
  requestedAt: string;
  approvedByUserId: string | null;
  approvedAt: string | null;
  applied: boolean;
}

export interface RequestAdjustmentInput {
  entityType: string;
  entityId: string;
  afterValue: number;
  reason: string;
}

export interface CodReconciliationSummary {
  expectedCod: number;
  collectedCod: number;
  riderDepositedCod: number;
  branchReceivedCod: number;
  merchantPayableCod: number;
  totalCourierFees: number;
}

export type CodFlowStageKey = "expected" | "collected" | "riderDeposited" | "branchReconciled" | "financeVerified" | "settled";

export interface CodFlowStage {
  key: CodFlowStageKey;
  amount: number;
  count: number;
}

export interface CodFlow {
  stages: CodFlowStage[];
}

export interface RiderCashHolding {
  riderId: string;
  branchId: string;
  parcelCount: number;
  amount: number;
  oldestCollectedAt: string;
  latestCollectedAt: string;
}

export interface RiderCash {
  items: RiderCashHolding[];
}
