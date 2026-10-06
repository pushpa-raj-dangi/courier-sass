import { describe, expect, it } from "vitest";
import type { ShipmentEvent } from "@/features/shipments/types";
import { buildCustodyChain, custodySince, formatCustodyStart, formatDuration } from "./custodyChain";

function ev(id: string, timestamp: string, holder: Partial<Pick<ShipmentEvent, "branchId" | "userId" | "riderId">>): ShipmentEvent {
  return { id, eventType: "Sorted", previousStatus: null, newStatus: "Sorted", branchId: null, userId: null, riderId: null, timestamp, notes: null, ...holder };
}

describe("buildCustodyChain", () => {
  const events = [
    ev("3", "2026-10-05T08:00:00Z", { branchId: "ktm", userId: "u2" }),
    ev("1", "2026-10-05T06:00:00Z", { riderId: "r1" }),
    ev("2", "2026-10-05T07:00:00Z", { branchId: "ktm", userId: "u1" }),
    ev("4", "2026-10-05T09:00:00Z", { riderId: "r2", branchId: "ktm" }),
  ];

  it("sorts, collapses same-branch staff events and splits on rider change", () => {
    const chain = buildCustodyChain(events);
    expect(chain.map((s) => s.holderKey)).toEqual(["rider:r1", "branch:ktm", "rider:r2"]);
    expect(chain[1]).toMatchObject({ from: "2026-10-05T07:00:00Z", to: "2026-10-05T09:00:00Z", eventCount: 2, userId: "u2" });
    expect(chain[2].to).toBeNull();
  });

  it("returns an empty chain for no events", () => {
    expect(buildCustodyChain([])).toEqual([]);
    expect(custodySince([])).toBeNull();
  });

  it("custodySince is the start of the current segment", () => {
    expect(custodySince(events)).toBe("2026-10-05T09:00:00Z");
  });

  it("formats durations", () => {
    expect(formatDuration("2026-10-05T08:00:00Z", "2026-10-05T10:18:00Z")).toBe("2 h 18 m");
    expect(formatDuration("2026-10-05T08:00:00Z", "2026-10-05T08:05:00Z")).toBe("5 m");
    expect(formatDuration("2026-10-03T08:00:00Z", "2026-10-05T10:00:00Z")).toBe("2 d 2 h");
    expect(formatDuration("2026-10-05T08:00:00Z", null, new Date("2026-10-05T09:00:00Z"))).toBe("1 h 0 m");
  });
});

describe("formatCustodyStart", () => {
  const time = (d: Date) => d.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  const date = (d: Date) => d.toLocaleDateString([], { day: "numeric", month: "short" });

  it("shows only the time when custody started today", () => {
    const now = new Date(2026, 9, 6, 15, 0);
    const since = new Date(2026, 9, 6, 9, 30);
    expect(formatCustodyStart(since.toISOString(), now)).toBe(time(since));
  });

  it("includes the date when custody started before today", () => {
    const now = new Date(2026, 9, 6, 15, 0);
    const since = new Date(2026, 9, 3, 9, 30);
    expect(formatCustodyStart(since.toISOString(), now)).toBe(`${date(since)}, ${time(since)}`);
  });
});
