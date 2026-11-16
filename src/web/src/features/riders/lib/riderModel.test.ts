import { describe, expect, it } from "vitest";
import type { RiderBoardRow, Vehicle } from "@/features/riders/types";
import {
  RIDER_STAGES, cashByRider, filterBoardRows, fleetVehicles, formatMinutesAgo, isLocationStale, lastSeenText,
  parseFleetFilter, parseRiderRole, parseRiderStage, parseRidersTab, progressShare, riderDayStage, riderInitials,
  riderStageCounts, roleLabel, staleOnRunCount,
} from "./riderModel";

const NOW = new Date("2026-10-16T08:00:00Z");
const minutesAgo = (m: number) => new Date(NOW.getTime() - m * 60_000).toISOString();

function row(overrides: Partial<RiderBoardRow>): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: null, run: null, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0, lastPingAt: null, ...overrides,
  };
}

describe("riderModel", () => {
  it("maps runs to the three day stages, in strip order", () => {
    expect(RIDER_STAGES.map((s) => s.label)).toEqual(["Off duty", "On run", "Run done"]);
    expect(riderDayStage(row({}))).toBe("off");
    expect(riderDayStage(row({ run: { id: "x", status: "Active" } }))).toBe("on");
    expect(riderDayStage(row({ run: { id: "x", status: "Completed" } }))).toBe("done");
  });

  it("treats no ping today and pings older than 15 minutes as stale", () => {
    expect(isLocationStale(null, NOW)).toBe(true);
    expect(isLocationStale(minutesAgo(15), NOW)).toBe(false);
    expect(isLocationStale(minutesAgo(16), NOW)).toBe(true);
  });

  it("words last-seen times and marks stale ones with text", () => {
    expect(formatMinutesAgo(minutesAgo(0), NOW)).toBe("just now");
    expect(formatMinutesAgo(minutesAgo(4), NOW)).toBe("4 min ago");
    expect(formatMinutesAgo(minutesAgo(125), NOW)).toBe("2 h 5 min ago");
    expect(formatMinutesAgo(minutesAgo(120), NOW)).toBe("2 h ago");
    expect(lastSeenText(null, NOW)).toEqual({ text: "No ping today", stale: true });
    expect(lastSeenText(minutesAgo(4), NOW)).toEqual({ text: "4 min ago", stale: false });
    expect(lastSeenText(minutesAgo(18), NOW)).toEqual({ text: "18 min ago · stale", stale: true });
  });

  it("counts stages over active riders only, and stale on-run riders", () => {
    const rows = [
      row({ riderId: "a" }),
      row({ riderId: "b", run: { id: "1", status: "Active" }, lastPingAt: minutesAgo(2) }),
      row({ riderId: "c", run: { id: "2", status: "Active" }, lastPingAt: null }),
      row({ riderId: "d", run: { id: "3", status: "Active" }, lastPingAt: minutesAgo(40) }),
      row({ riderId: "e", run: { id: "4", status: "Completed" } }),
      row({ riderId: "f", isActive: false }),
    ];
    expect(riderStageCounts(rows)).toEqual({ off: 1, on: 3, done: 1 });
    expect(staleOnRunCount(rows, NOW)).toBe(2);
  });

  it("parses URL values with safe defaults", () => {
    expect(parseRidersTab(null)).toBe("riders");
    expect(parseRidersTab("map")).toBe("map");
    expect(parseRidersTab("bogus")).toBe("riders");
    expect(parseRiderStage("on")).toBe("on");
    expect(parseRiderStage("planned")).toBeUndefined();
    expect(parseRiderRole("Pickup")).toBe("Pickup");
    expect(parseRiderRole("PickupRider")).toBeUndefined();
    expect(parseFleetFilter("unassigned")).toBe("unassigned");
    expect(parseFleetFilter(null)).toBe("all");
  });

  it("filters rows by stage, role and the inactive toggle", () => {
    const rows = [
      row({ riderId: "a", roleType: "PickupRider" }),
      row({ riderId: "b", run: { id: "1", status: "Active" } }),
      row({ riderId: "c", isActive: false }),
    ];
    expect(filterBoardRows(rows, { showInactive: false }).map((r) => r.riderId)).toEqual(["a", "b"]);
    expect(filterBoardRows(rows, { showInactive: true }).map((r) => r.riderId)).toEqual(["a", "b", "c"]);
    expect(filterBoardRows(rows, { stage: "on", showInactive: false }).map((r) => r.riderId)).toEqual(["b"]);
    expect(filterBoardRows(rows, { role: "Pickup", showInactive: false }).map((r) => r.riderId)).toEqual(["a"]);
    expect(roleLabel("PickupRider")).toBe("Pickup rider");
  });

  it("never produces NaN progress and builds initials", () => {
    expect(progressShare(0, 0)).toBe(0);
    expect(progressShare(3, 4)).toBe(0.75);
    expect(progressShare(5, 4)).toBe(1);
    expect(riderInitials("Asha Kumari Gurung")).toBe("AG");
    expect(riderInitials("bikash")).toBe("B");
    expect(riderInitials("  ")).toBe("?");
  });

  it("sums cash per rider across branches and keeps the oldest collection", () => {
    const totals = cashByRider([
      { riderId: "r1", branchId: "b1", parcelCount: 2, amount: 1500, oldestCollectedAt: "2026-10-15T10:00:00Z", latestCollectedAt: "2026-10-15T12:00:00Z" },
      { riderId: "r1", branchId: "b2", parcelCount: 1, amount: 500, oldestCollectedAt: "2026-10-14T09:00:00Z", latestCollectedAt: "2026-10-14T09:00:00Z" },
      { riderId: "r2", branchId: "b1", parcelCount: 1, amount: 250, oldestCollectedAt: "2026-10-16T07:00:00Z", latestCollectedAt: "2026-10-16T07:00:00Z" },
    ]);
    expect(totals.get("r1")).toEqual({ amount: 2000, parcelCount: 3, oldestCollectedAt: "2026-10-14T09:00:00Z" });
    expect(totals.get("r2")?.amount).toBe(250);
  });

  it("shows unassigned vehicles always and assigned ones only for board riders when a branch is picked", () => {
    const vehicles: Vehicle[] = [
      { id: "v1", riderId: null, type: "Bike", plateNumber: "BA 1", capacity: null },
      { id: "v2", riderId: "r1", type: "Bike", plateNumber: "BA 2", capacity: 20 },
      { id: "v3", riderId: "elsewhere", type: "Van", plateNumber: "BA 3", capacity: 500 },
    ];
    const ids = new Set(["r1"]);
    expect(fleetVehicles(vehicles, ids, true).map((v) => v.id)).toEqual(["v1", "v2"]);
    expect(fleetVehicles(vehicles, ids, false).map((v) => v.id)).toEqual(["v1", "v2", "v3"]);
  });
});
