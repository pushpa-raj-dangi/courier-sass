import { describe, expect, it } from "vitest";
import type { MerchantSettlement, MerchantSettlementItem } from "@/features/merchant-cod/types";
import { filterByPeriod, settlementTotals } from "./settlementTotals";

const item = (cod: number, fee: number, other: number, net: number): MerchantSettlementItem => ({
  id: "i", shipmentId: "s", shipmentAwb: "A", codAmount: cod, courierFee: fee, otherDeductions: other, netPayable: net,
});

const NOW = new Date("2026-11-17T00:00:00Z");
const daysAgo = (n: number) => new Date(NOW.getTime() - n * 86_400_000).toISOString();
const settlement = (id: string, periodTo: string): MerchantSettlement => ({
  id, merchantId: "m", periodFrom: periodTo, periodTo, totalPayable: 0, status: "Created", settledAt: null, items: [],
});

describe("settlementTotals", () => {
  it("sums each column", () => {
    expect(settlementTotals([item(2500, 150, 0, 2350), item(1000, 100, 20, 880)])).toEqual({ cod: 3500, fees: 250, other: 20, net: 3230 });
  });
  it("is zero for no items", () => {
    expect(settlementTotals([])).toEqual({ cod: 0, fees: 0, other: 0, net: 0 });
  });
});

describe("filterByPeriod", () => {
  const list = [settlement("recent", daysAgo(20)), settlement("old", daysAgo(40)), settlement("ancient", daysAgo(120))];
  it("30d keeps 20 days ago, drops 40", () => {
    expect(filterByPeriod(list, "30d", NOW).map((s) => s.id)).toEqual(["recent"]);
  });
  it("90d keeps 40, drops 120", () => {
    expect(filterByPeriod(list, "90d", NOW).map((s) => s.id)).toEqual(["recent", "old"]);
  });
  it("all keeps everything", () => {
    expect(filterByPeriod(list, "all", NOW)).toHaveLength(3);
  });
});
