import type { MerchantSettlement, MerchantSettlementItem } from "@/features/merchant-cod/types";

export type SettlementPeriod = "30d" | "90d" | "all";

export function isSettlementPeriod(value: string | null): value is SettlementPeriod {
  return value === "30d" || value === "90d" || value === "all";
}

/** COD collected, courier fees, other deductions and the net the merchant receives. */
export function settlementTotals(items: MerchantSettlementItem[]): { cod: number; fees: number; other: number; net: number } {
  return items.reduce(
    (t, i) => ({
      cod: t.cod + i.codAmount,
      fees: t.fees + i.courierFee,
      other: t.other + i.otherDeductions,
      net: t.net + i.netPayable,
    }),
    { cod: 0, fees: 0, other: 0, net: 0 }
  );
}

const DAY_MS = 24 * 60 * 60 * 1000;

/** Keeps settlements whose period ended within the window (by `periodTo`). */
export function filterByPeriod(settlements: MerchantSettlement[], period: SettlementPeriod, now: Date): MerchantSettlement[] {
  if (period === "all") return settlements;
  const days = period === "30d" ? 30 : 90;
  const cutoff = now.getTime() - days * DAY_MS;
  return settlements.filter((s) => new Date(s.periodTo).getTime() >= cutoff);
}
