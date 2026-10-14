import type { RiderCashHolding } from "@/features/cod/types";

const grouping = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 });
const dateFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" });
const timeFormat = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

/** "Rs 12,48,500" — Nepali/Indian lakh grouping, up to 2 decimals. */
export function formatRs(amount: number): string {
  return `Rs ${grouping.format(amount)}`;
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

export const HELD_WARNING_HOURS = 24;

export function hoursHeld(oldestIso: string, now: Date = new Date()): number {
  return Math.max(0, (now.getTime() - Date.parse(oldestIso)) / 3_600_000);
}

export function formatHeldFor(oldestIso: string, now: Date = new Date()): string {
  const hours = Math.floor(hoursHeld(oldestIso, now));
  if (hours < 1) return "< 1 h";
  if (hours < 24) return `${hours} h`;
  const days = Math.floor(hours / 24);
  const rest = hours % 24;
  return rest === 0 ? `${days} d` : `${days} d ${rest} h`;
}

/** Longest-held first (so every "held over 24 h" row is on top). */
export function sortHoldings(items: RiderCashHolding[]): RiderCashHolding[] {
  return [...items].sort((a, b) => Date.parse(a.oldestCollectedAt) - Date.parse(b.oldestCollectedAt));
}
