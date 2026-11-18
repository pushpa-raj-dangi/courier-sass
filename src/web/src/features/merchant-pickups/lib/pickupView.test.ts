import { describe, expect, it } from "vitest";
import type { PickupParcel, PickupRequest, PickupStage } from "../types";
import { isOpenStage, parcelsCell, pickupTiles, splitPickups } from "./pickupView";

const now = new Date("2026-10-31T23:30:00Z"); // 2026-11-01 in Kathmandu

const parcels = (n: number): PickupParcel[] =>
  Array.from({ length: n }, (_, i) => ({ id: `s${i}`, awb: `AWB${i}`, destinationArea: null, codAmount: null }));

function pk(
  id: string,
  stage: PickupStage,
  date: string,
  n = 1,
  status: PickupRequest["status"] = "Requested",
  branchName: string | null = "KTM",
): PickupRequest {
  return {
    id, merchantId: "m", branchId: "b", shipmentIds: [], requestedForDate: date, notes: null,
    status, createdAt: "", updatedAt: "", stage, branchName, shipments: parcels(n),
  };
}

describe("pickupView", () => {
  it("isOpenStage", () => {
    expect(isOpenStage("Requested")).toBe(true);
    expect(isOpenStage("Scheduled")).toBe(true);
    expect(isOpenStage("Collected")).toBe(false);
    expect(isOpenStage("Cancelled")).toBe(false);
  });

  it("splits upcoming (date asc, overdue first) and past (date desc)", () => {
    const list = [
      pk("a", "Scheduled", "2026-11-05"),
      pk("b", "Collected", "2026-10-20"),
      pk("c", "Requested", "2026-10-30"),
      pk("d", "Cancelled", "2026-10-25"),
      pk("e", "Requested", "2026-11-02"),
    ];
    const { upcoming, past } = splitPickups(list, now);
    expect(upcoming.map((p) => p.id)).toEqual(["c", "e", "a"]);
    expect(past.map((p) => p.id)).toEqual(["d", "b"]);
  });

  it("next ignores overdue open and closed pickups; null when none", () => {
    const list = [
      pk("late", "Requested", "2026-10-30"),
      pk("done", "Collected", "2026-11-02"),
      pk("n2", "Scheduled", "2026-11-04", 3, "Scheduled", "PKR"),
      pk("n1", "Requested", "2026-11-03", 2),
    ];
    expect(pickupTiles(list, now).next).toEqual({ date: "2026-11-03", branchName: "KTM", parcels: 2 });
    expect(pickupTiles([pk("late", "Requested", "2026-10-30"), pk("x", "Cancelled", "2026-11-03")], now).next).toBeNull();
  });

  it("waiting counts stage Requested only", () => {
    const list = [
      pk("a", "Requested", "2026-11-03"),
      pk("b", "Scheduled", "2026-11-03", 1, "Requested"),
      pk("c", "Scheduled", "2026-11-03"),
    ];
    expect(pickupTiles(list, now).waiting).toBe(1);
  });

  it("collectedThisMonth uses the Kathmandu month", () => {
    const list = [
      pk("a", "Collected", "2026-11-01", 3),
      pk("b", "Collected", "2026-10-31", 5),
      pk("c", "Requested", "2026-11-01", 2),
    ];
    expect(pickupTiles(list, now).collectedThisMonth).toEqual({ pickups: 1, parcels: 3 });
  });

  it("parcelsCell shows first two AWBs", () => {
    expect(parcelsCell(pk("a", "Requested", "2026-11-01", 5))).toEqual({ count: 5, awbs: ["AWB0", "AWB1"], more: 3 });
    expect(parcelsCell(pk("a", "Requested", "2026-11-01", 0))).toEqual({ count: 0, awbs: [], more: 0 });
  });
});
