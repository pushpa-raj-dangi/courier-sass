import { describe, expect, it } from "vitest";
import { buildJourney, formatEventTime } from "@/features/tracking/lib/journey";
import { headlineFor } from "@/features/tracking/lib/statusMeta";
import type {
  PublicTrackingResult,
  PublicTrackingStatus,
} from "@/features/tracking/types";

function result(
  currentStatus: PublicTrackingStatus,
  events: { status: PublicTrackingStatus; timestamp?: string; location?: string | null }[],
): PublicTrackingResult {
  return {
    awb: "AWB1",
    currentStatus,
    destinationArea: null,
    senderName: null,
    events: events.map((e, i) => ({
      status: e.status,
      timestamp: e.timestamp ?? `2026-09-29T0${i}:00:00Z`,
      location: e.location ?? null,
    })),
  };
}

describe("buildJourney", () => {
  it("received only", () => {
    const steps = buildJourney(result("Received", [{ status: "Received" }]));
    expect(steps).toHaveLength(6);
    expect(steps[0].state).toBe("now");
    expect(steps.slice(1).every((s) => s.state === "next")).toBe(true);
    expect(steps.every((s) => !s.alert)).toBe(true);
  });

  it("at destination with location", () => {
    const steps = buildJourney(
      result("AtDestinationBranch", [
        { status: "Received" },
        { status: "PickedUp" },
        { status: "InTransit" },
        { status: "AtDestinationBranch", location: "Lalitpur Branch" },
      ]),
    );
    expect(steps.slice(0, 3).every((s) => s.state === "done")).toBe(true);
    expect(steps[3].state).toBe("now");
    expect(steps[3].label).toBe("Arrived at Lalitpur Branch");
  });

  const base = [
    { status: "Received" },
    { status: "PickedUp" },
    { status: "InTransit" },
    { status: "AtDestinationBranch" },
  ] as const;

  it("reattempt uses the latest OFD event", () => {
    const steps = buildJourney(
      result("OutForDelivery", [
        ...base,
        { status: "OutForDelivery", timestamp: "2026-09-29T10:00:00Z" },
        { status: "DeliveryAttemptUnsuccessful", timestamp: "2026-09-29T12:00:00Z" },
        { status: "OutForDelivery", timestamp: "2026-09-30T09:00:00Z" },
      ]),
    );
    expect(steps[4].state).toBe("now");
    expect(steps[4].alert).toBe(false);
    expect(steps[4].timestamp).toBe("2026-09-30T09:00:00Z");
  });

  it("failed attempt current", () => {
    const steps = buildJourney(
      result("DeliveryAttemptUnsuccessful", [
        ...base,
        { status: "OutForDelivery", timestamp: "2026-09-29T10:00:00Z" },
        { status: "DeliveryAttemptUnsuccessful", timestamp: "2026-09-29T12:00:00Z" },
      ]),
    );
    expect(steps[4].state).toBe("now");
    expect(steps[4].alert).toBe(true);
    expect(steps[4].label).toBe("Delivery attempt unsuccessful");
    expect(steps[4].timestamp).toBe("2026-09-29T12:00:00Z");
    expect(steps[5].state).toBe("next");
  });

  it("delivered with no AtDestinationBranch event", () => {
    const steps = buildJourney(
      result("Delivered", [
        { status: "Received" },
        { status: "PickedUp" },
        { status: "InTransit" },
        { status: "OutForDelivery" },
        { status: "Delivered" },
      ]),
    );
    expect(steps).toHaveLength(6);
    expect(steps.every((s) => s.state === "done")).toBe(true);
    expect(steps[3].timestamp).toBeNull();
    expect(steps[3].label).toBe("Arrived at local branch");
  });

  it("cancelled after Received only", () => {
    const steps = buildJourney(
      result("Cancelled", [{ status: "Received" }, { status: "Cancelled" }]),
    );
    expect(steps).toHaveLength(1);
    expect(steps[0].stage).toBe("Received");
    expect(steps[0].state).toBe("done");
  });

  it("cancelled with no events yields no steps", () => {
    expect(buildJourney(result("Cancelled", []))).toEqual([]);
  });

  it("returning to sender shows only reached stages, all done, and never a pending delivery", () => {
    const steps = buildJourney(
      result("ReturningToSender", [
        ...base,
        { status: "OutForDelivery" },
        { status: "DeliveryAttemptUnsuccessful" },
        { status: "ReturningToSender" },
      ]),
    );
    expect(steps.map((s) => s.stage)).toEqual([
      "Received",
      "PickedUp",
      "InTransit",
      "AtDestinationBranch",
      "OutForDelivery",
    ]);
    expect(steps.every((s) => s.state === "done")).toBe(true);
    expect(steps.every((s) => !s.alert)).toBe(true);
    expect(steps.some((s) => s.stage === "Delivered")).toBe(false);
  });
});

describe("headlineFor", () => {
  const ofd = (timestamp: string) =>
    result("OutForDelivery", [{ status: "Received" }, { status: "OutForDelivery", timestamp }]);

  it("says Arriving today when the latest OFD event is on the viewer's local today", () => {
    // Local-time constructors keep this independent of the test runner's time zone.
    const now = new Date(2026, 8, 30, 18, 0);
    const sameDay = new Date(2026, 8, 30, 8, 0).toISOString();
    expect(headlineFor(ofd(sameDay), now)).toBe("Arriving today");
  });

  it("falls back to Out for delivery when the OFD event was on an earlier day", () => {
    const now = new Date(2026, 8, 30, 9, 0);
    const yesterday = new Date(2026, 8, 29, 17, 0).toISOString();
    expect(headlineFor(ofd(yesterday), now)).toBe("Out for delivery");
  });

  it("uses the latest OFD event on a reattempt", () => {
    const now = new Date(2026, 8, 30, 12, 0);
    const r = result("OutForDelivery", [
      { status: "OutForDelivery", timestamp: new Date(2026, 8, 29, 9, 0).toISOString() },
      { status: "DeliveryAttemptUnsuccessful", timestamp: new Date(2026, 8, 29, 12, 0).toISOString() },
      { status: "OutForDelivery", timestamp: new Date(2026, 8, 30, 9, 0).toISOString() },
    ]);
    expect(headlineFor(r, now)).toBe("Arriving today");
  });

  it("uses the static headline for other statuses", () => {
    const now = new Date(2026, 8, 30, 12, 0);
    expect(headlineFor(result("ReturningToSender", []), now)).toBe("Returning to the seller");
    expect(headlineFor(result("InTransit", []), now)).toBe("On its way");
  });
});

describe("formatEventTime", () => {
  it("includes a clock time", () => {
    expect(formatEventTime("2026-09-29T05:17:00Z")).toMatch(/\d{1,2}:\d{2}/);
  });
});
