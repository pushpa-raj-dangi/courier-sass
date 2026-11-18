import { kathmanduToday } from "@/lib/dates/kathmandu";
import type { PickupRequest, PickupStage } from "../types";

export type PickupTab = "upcoming" | "past";

export function isOpenStage(stage: PickupStage): boolean {
  return stage === "Requested" || stage === "Scheduled";
}

const byDateAsc = (a: PickupRequest, b: PickupRequest) => a.requestedForDate.localeCompare(b.requestedForDate);

// Dates are plain yyyy-MM-dd strings: compared as strings, never via Date.
export function splitPickups(list: PickupRequest[], _now: Date = new Date()): { upcoming: PickupRequest[]; past: PickupRequest[] } {
  return {
    upcoming: list.filter((p) => isOpenStage(p.stage)).sort(byDateAsc),
    past: list.filter((p) => !isOpenStage(p.stage)).sort((a, b) => byDateAsc(b, a)),
  };
}

export function pickupTiles(list: PickupRequest[], now: Date = new Date()) {
  const today = kathmanduToday(now);
  const month = today.slice(0, 7);

  const next = list.filter((p) => isOpenStage(p.stage) && p.requestedForDate >= today).sort(byDateAsc)[0];
  const collected = list.filter((p) => p.stage === "Collected" && p.requestedForDate.startsWith(month));

  return {
    next: next ? { date: next.requestedForDate, branchName: next.branchName, parcels: next.shipments.length } : null,
    waiting: list.filter((p) => p.stage === "Requested").length,
    collectedThisMonth: {
      pickups: collected.length,
      parcels: collected.reduce((sum, p) => sum + p.shipments.length, 0),
    },
  };
}

export function parcelsCell(p: PickupRequest): { count: number; awbs: string[]; more: number } {
  const count = p.shipments.length;
  return { count, awbs: p.shipments.slice(0, 2).map((s) => s.awb), more: Math.max(0, count - 2) };
}

const dayFormat = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short" });

/** "Tue, 18 Nov" from a plain yyyy-MM-dd; anchored at local noon so the day never shifts. */
export function pickupDayLabel(isoDate: string): string {
  return dayFormat.format(new Date(`${isoDate}T12:00:00`)).replace(/^(\w+) /, "$1, ");
}
