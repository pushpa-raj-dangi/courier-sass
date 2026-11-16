import type { RiderCashHolding } from "@/features/cod/types";
import type { RiderBoardRow, RiderRoleType, Vehicle } from "@/features/riders/types";

export type RiderStage = "off" | "on" | "done";

/** The backend has two run states (Active, Completed); no run today is "off". */
export const RIDER_STAGES: { key: RiderStage; label: string; token: string }[] = [
  { key: "off", label: "Off duty", token: "status-created" },
  { key: "on", label: "On run", token: "status-out-for-delivery" },
  { key: "done", label: "Run done", token: "status-delivered" },
];

export const STALE_LOCATION_MINUTES = 15;

export function riderDayStage(row: Pick<RiderBoardRow, "run">): RiderStage {
  if (!row.run) return "off";
  return row.run.status === "Active" ? "on" : "done";
}

export function riderStageLabel(stage: RiderStage): string {
  return RIDER_STAGES.find((s) => s.key === stage)?.label ?? stage;
}

export function riderStageToken(stage: RiderStage): string {
  return RIDER_STAGES.find((s) => s.key === stage)?.token ?? "status-created";
}

export function minutesSince(iso: string, now: Date = new Date()): number {
  return Math.max(0, Math.floor((now.getTime() - Date.parse(iso)) / 60_000));
}

/** No ping today counts as stale: the rider cannot be placed. */
export function isLocationStale(lastPingAt: string | null, now: Date = new Date()): boolean {
  return lastPingAt === null || minutesSince(lastPingAt, now) > STALE_LOCATION_MINUTES;
}

/** "just now", "4 min ago", "2 h 5 min ago". */
export function formatMinutesAgo(iso: string, now: Date = new Date()): string {
  const minutes = minutesSince(iso, now);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} h ago` : `${hours} h ${rest} min ago`;
}

/** Last-seen column text; a stale ping says so in words, not only by colour. */
export function lastSeenText(lastPingAt: string | null, now: Date = new Date()): { text: string; stale: boolean } {
  if (lastPingAt === null) return { text: "No ping today", stale: true };
  const stale = isLocationStale(lastPingAt, now);
  const ago = formatMinutesAgo(lastPingAt, now);
  return { text: stale ? `${ago} · stale` : ago, stale };
}

/** Strip counts: inactive riders are not part of today's operation. */
export function riderStageCounts(rows: RiderBoardRow[]): Record<RiderStage, number> {
  const counts: Record<RiderStage, number> = { off: 0, on: 0, done: 0 };
  for (const row of rows) {
    if (row.isActive) counts[riderDayStage(row)] += 1;
  }
  return counts;
}

export function staleOnRunCount(rows: RiderBoardRow[], now: Date = new Date()): number {
  return rows.filter((r) => r.isActive && riderDayStage(r) === "on" && isLocationStale(r.lastPingAt, now)).length;
}

export type RidersTab = "riders" | "map" | "fleet";

export function parseRidersTab(value: string | null): RidersTab {
  return value === "map" || value === "fleet" ? value : "riders";
}

export function parseRiderStage(value: string | null): RiderStage | undefined {
  return RIDER_STAGES.find((s) => s.key === value)?.key;
}

export type RiderRoleFilter = "Pickup" | "Delivery";

export function parseRiderRole(value: string | null): RiderRoleFilter | undefined {
  return value === "Pickup" || value === "Delivery" ? value : undefined;
}

export function roleLabel(roleType: RiderRoleType): string {
  return roleType === "PickupRider" ? "Pickup rider" : "Delivery rider";
}

export function filterBoardRows(
  rows: RiderBoardRow[],
  filters: { stage?: RiderStage; role?: RiderRoleFilter; showInactive: boolean },
): RiderBoardRow[] {
  return rows.filter((row) => {
    if (!filters.showInactive && !row.isActive) return false;
    if (filters.stage && riderDayStage(row) !== filters.stage) return false;
    if (filters.role && row.roleType !== `${filters.role}Rider`) return false;
    return true;
  });
}

/** Share of a run done, 0–1. A run with no tasks is 0, never NaN. */
export function progressShare(done: number, total: number): number {
  return total > 0 ? Math.min(1, done / total) : 0;
}

export function riderInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  const first = parts[0][0];
  const last = parts.length > 1 ? parts[parts.length - 1][0] : "";
  return `${first}${last}`.toUpperCase();
}

export interface RiderCashTotal {
  amount: number;
  parcelCount: number;
  oldestCollectedAt: string;
}

/** One total per rider: a rider can hold cash collected for more than one branch. */
export function cashByRider(items: RiderCashHolding[]): Map<string, RiderCashTotal> {
  const totals = new Map<string, RiderCashTotal>();
  for (const item of items) {
    const current = totals.get(item.riderId);
    if (!current) {
      totals.set(item.riderId, { amount: item.amount, parcelCount: item.parcelCount, oldestCollectedAt: item.oldestCollectedAt });
      continue;
    }
    totals.set(item.riderId, {
      amount: current.amount + item.amount,
      parcelCount: current.parcelCount + item.parcelCount,
      oldestCollectedAt:
        Date.parse(item.oldestCollectedAt) < Date.parse(current.oldestCollectedAt) ? item.oldestCollectedAt : current.oldestCollectedAt,
    });
  }
  return totals;
}

export type FleetFilter = "all" | "unassigned";

export function parseFleetFilter(value: string | null): FleetFilter {
  return value === "unassigned" ? "unassigned" : "all";
}

/**
 * GET /vehicles is not branch-scoped (backend follow-up). With a branch picked, keep unassigned
 * vehicles and those assigned to riders on the (branch-filtered) board.
 */
export function fleetVehicles(vehicles: Vehicle[], boardRiderIds: Set<string>, branchPicked: boolean): Vehicle[] {
  if (!branchPicked) return vehicles;
  return vehicles.filter((v) => v.riderId === null || boardRiderIds.has(v.riderId));
}
