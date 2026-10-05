import { describe, expect, it } from "vitest";
import { buildProgress, PROGRESS_STEPS } from "./progress";

const states = (status: string) => buildProgress(status).steps.map((s) => s.state);

describe("buildProgress", () => {
  it("has seven forward steps", () => {
    expect(PROGRESS_STEPS.map((s) => s.label)).toEqual([
      "Booked", "Picked up", "Origin hub", "In transit", "Destination", "Out for delivery", "Delivered",
    ]);
  });

  it("marks earlier steps complete and the current one current", () => {
    expect(states("OutForDelivery")).toEqual(["complete", "complete", "complete", "complete", "complete", "current", "upcoming"]);
    expect(states("Created")).toEqual(["current", "upcoming", "upcoming", "upcoming", "upcoming", "upcoming", "upcoming"]);
    expect(states("Sorted")[2]).toBe("current");
  });

  it("marks everything complete when delivered", () => {
    expect(states("Delivered").every((s) => s === "complete")).toBe(true);
    expect(buildProgress("Delivered").exception).toBeNull();
  });

  it("stops at out-for-delivery with an NDR marker", () => {
    const p = buildProgress("NdrCreated");
    expect(p.exception).toBe("ndr");
    expect(p.exceptionAfter).toBe(5);
    expect(p.steps[6].state).toBe("upcoming");
    expect(p.steps.some((s) => s.state === "current")).toBe(false);
  });

  it("shows RTO after the furthest forward step and never claims delivery", () => {
    const p = buildProgress("RtoInTransit");
    expect(p.exception).toBe("rto");
    expect(p.steps[6].state).toBe("upcoming");
  });

  it("treats international and unknown statuses as in transit, cancelled as an exception", () => {
    expect(states("InInternationalTransit")[3]).toBe("current");
    expect(buildProgress("Cancelled").exception).toBe("cancelled");
  });
});
