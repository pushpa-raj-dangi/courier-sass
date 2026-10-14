import type { Adjustment, MerchantSettlement } from "@/features/cod/types";

export interface SettlementBreakdown {
  parcelCount: number;
  codTotal: number;
  courierFees: number;
  /** Already includes approved adjustments (applying one rewrites the item server-side). */
  otherDeductions: number;
  /** Net change in deductions from applied adjustments on this settlement's items. */
  adjustmentsIncluded: number;
  /** The server's totalPayable — the only figure shown as "Merchant payable". */
  payable: number;
  /** Σ item netPayable, compared against payable. */
  linesNet: number;
  mismatch: boolean;
}

const cents = (n: number) => Math.round(n * 100);

export function buildSettlementBreakdown(settlement: MerchantSettlement, adjustments: Adjustment[]): SettlementBreakdown {
  const items = settlement.items;
  const sum = (pick: (i: (typeof items)[number]) => number) => items.reduce((total, i) => total + pick(i), 0);
  const itemIds = new Set(items.map((i) => i.id));
  const adjustmentsIncluded = adjustments
    .filter((a) => a.applied && a.entityType === "MerchantSettlementItem" && itemIds.has(a.entityId))
    .reduce((total, a) => total + (a.afterValue - a.beforeValue), 0);
  const linesNet = sum((i) => i.netPayable);
  return {
    parcelCount: items.length,
    codTotal: sum((i) => i.codAmount),
    courierFees: sum((i) => i.courierFee),
    otherDeductions: sum((i) => i.otherDeductions),
    adjustmentsIncluded,
    payable: settlement.totalPayable,
    linesNet,
    mismatch: cents(linesNet) !== cents(settlement.totalPayable),
  };
}
