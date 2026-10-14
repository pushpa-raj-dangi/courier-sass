import { describe, expect, it } from "vitest";
import { buildSettlementBreakdown } from "./settlementBreakdown";
import type { Adjustment, MerchantSettlement } from "@/features/cod/types";

const settlement = (items: MerchantSettlement["items"], totalPayable: number): MerchantSettlement => ({
  id: "ms1", merchantId: "m1", periodFrom: "2026-10-01T00:00:00Z", periodTo: "2026-10-07T00:00:00Z", totalPayable, status: "Created", settledAt: null, items,
});
const item = (id: string, cod: number, fee: number, other: number) => ({ id, shipmentId: `s-${id}`, shipmentAwb: `AWB-${id}`, codAmount: cod, courierFee: fee, otherDeductions: other, netPayable: cod - fee - other });
const adjustment = (entityId: string, before: number, after: number, applied = true): Adjustment => ({
  id: `a-${entityId}`, entityType: "MerchantSettlementItem", entityId, beforeValue: before, afterValue: after, reason: "r", requestedByUserId: "u", requestedAt: "2026-10-06T00:00:00Z", approvedByUserId: applied ? "u2" : null, approvedAt: applied ? "2026-10-06T01:00:00Z" : null, applied,
});

describe("buildSettlementBreakdown", () => {
  it("sums lines and agrees with the server payable", () => {
    const b = buildSettlementBreakdown(settlement([item("1", 2500, 150, 0), item("2", 1000, 100, 50)], 3200), []);
    expect(b).toMatchObject({ parcelCount: 2, codTotal: 3500, courierFees: 250, otherDeductions: 50, adjustmentsIncluded: 0, payable: 3200, linesNet: 3200, mismatch: false });
  });

  it("reports approved adjustments on this settlement's items only, as an included amount", () => {
    const b = buildSettlementBreakdown(settlement([item("1", 2500, 150, 50)], 2300), [
      adjustment("1", 0, 50), adjustment("1", 0, 999, false), adjustment("other", 0, 70),
    ]);
    expect(b.adjustmentsIncluded).toBe(50);
    expect(b.otherDeductions).toBe(50);
  });

  it("flags a mismatch instead of hiding it", () => {
    const b = buildSettlementBreakdown(settlement([item("1", 2500, 150, 0)], 2000), []);
    expect(b.mismatch).toBe(true);
    expect(b.linesNet).toBe(2350);
    expect(b.payable).toBe(2000);
  });

  it("handles a settlement with no items", () => {
    const b = buildSettlementBreakdown(settlement([], 0), []);
    expect(b).toMatchObject({ parcelCount: 0, codTotal: 0, courierFees: 0, otherDeductions: 0, payable: 0, linesNet: 0, mismatch: false });
    expect(Number.isNaN(b.codTotal)).toBe(false);
  });
});
