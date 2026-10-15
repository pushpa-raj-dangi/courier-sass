import type { RiderCashHolding } from "@/features/cod/types";

const wholeRupees = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 0 });
const withPaise = new Intl.NumberFormat("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const dateFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" });
const timeFormat = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

/** "Rs 12,48,500" — Nepali/Indian lakh grouping; "Rs 2,350.50" only when there are non-zero paise. */
export function formatRs(amount: number): string {
  const paise = Math.round(amount * 100) + 0; // + 0 turns -0 into 0
  return `Rs ${paise % 100 === 0 ? wholeRupees.format(paise / 100) : withPaise.format(paise / 100)}`;
}

/** The parcel's AWB; the short shipment id only if the AWB lookup missed. */
export function collectionLabel(collection: { awb: string | null; shipmentId: string }): string {
  return collection.awb ?? collection.shipmentId.slice(0, 8);
}

export function formatDate(iso: string): string {
  return dateFormat.format(new Date(iso));
}

export function formatTime(iso: string): string {
  return timeFormat.format(new Date(iso));
}

export type VarianceTone = "matched" | "short" | "over";

/** Text always carries the meaning; `token` is a status colour for the dot only. */
export function describeVariance(variance: number): { tone: VarianceTone; label: string; token: string } {
  const rounded = Math.round(variance * 100) / 100;
  if (rounded === 0) return { tone: "matched", label: "Matched", token: "status-delivered" };
  if (rounded < 0) return { tone: "short", label: `Short − ${formatRs(-rounded)}`, token: "status-ndr" };
  return { tone: "over", label: `Over + ${formatRs(rounded)}`, token: "status-in-transit" };
}

/** Longest-held first (so every "held over 24 h" row is on top). */
export function sortHoldings(items: RiderCashHolding[]): RiderCashHolding[] {
  return [...items].sort((a, b) => Date.parse(a.oldestCollectedAt) - Date.parse(b.oldestCollectedAt));
}
