import { describe, expect, it } from "vitest";
import { buildPipelineStages, codSegments, formatRate, formatTimeSpan, openNdrCount, rangeLabel, rangeToApi } from "./dashboardModel";

describe("dashboardModel", () => {
  it("buckets current WIP into pipeline stages with tab links", () => {
    const stages = buildPipelineStages(
      { Created: 2, PickupAssigned: 1, PickedUp: 4, AtOriginBranch: 3, Sorted: 2, InTransit: 7, AtDestinationBranch: 5, OutForDelivery: 6, Delivered: 99 },
      { b1: { deliverySuccesses: 10 } as never, b2: { deliverySuccesses: 5 } as never }
    );
    expect(stages.map((s) => [s.label, s.count])).toEqual([
      ["Booked", 3], ["Picked up", 4], ["At origin", 5], ["In transit", 7], ["At destination", 5], ["Out for delivery", 6], ["Delivered", 15],
    ]);
    expect(stages[0].href).toBe("/shipments?status=Created&status=PickupAssigned");
    expect(stages[3].href).toBe("/shipments?tab=in-transit");
    expect(stages[6].href).toBeUndefined();
    expect(stages[6].caption).toBe("in range");
  });

  it("shows missing data as unknown, never as zero", () => {
    expect(buildPipelineStages(undefined, undefined).every((s) => s.count === null)).toBe(true);
    const noOrg = buildPipelineStages({ InTransit: 7 }, undefined);
    expect(noOrg.find((s) => s.key === "delivered")?.count).toBeNull();
    expect(noOrg.find((s) => s.key === "in-transit")?.count).toBe(7);
    expect(noOrg.find((s) => s.key === "booked")?.count).toBe(0);
  });

  it("splits COD into non-negative segments", () => {
    const segs = codSegments({ expected: 1000, collected: 700, pending: 0, settled: 900, merchantPayable: 0 });
    expect(segs.map((s) => [s.key, s.value])).toEqual([["settled", 900], ["collected", 0], ["uncollected", 300]]);
  });

  it("formats rates and .NET TimeSpans", () => {
    expect(formatRate(0.914)).toBe("91.4%");
    expect(formatRate(null)).toBe("—");
    expect(formatTimeSpan("1.19:12:00")).toBe("1.8 days");
    expect(formatTimeSpan("05:30:00.1234")).toBe("5.5 h");
    expect(formatTimeSpan(null)).toBe("—");
    expect(formatTimeSpan("garbage")).toBe("—");
  });

  it("counts non-resolved NDRs", () => {
    expect(openNdrCount({ Open: 2, Contacting: 1, Resolved: 9 })).toBe(3);
    expect(openNdrCount(undefined)).toBe(0);
  });

  it("labels and converts ranges", () => {
    const now = new Date(2026, 9, 5, 10);
    expect(rangeLabel({ preset: "7d", from: "", to: "" })).toBe("Last 7 days");
    expect(rangeToApi({ preset: "today", from: "", to: "" }, now)).toEqual({ from: "2026-10-05T00:00:00Z", to: "2026-10-05T23:59:59Z" });
    expect(rangeToApi({ preset: "custom", from: "2026-10-01", to: "" }, now)).toEqual({ from: "2026-10-01T00:00:00Z", to: undefined });
    expect(rangeLabel({ preset: "custom", from: "2026-10-01", to: "2026-10-03" })).toBe("2026-10-01 – 2026-10-03");
  });
});
