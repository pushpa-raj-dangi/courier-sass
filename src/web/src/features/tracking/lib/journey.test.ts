import { describe, expect, it } from "vitest";
import { buildJourney, formatEventTime } from "@/features/tracking/lib/journey";
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
});

describe("formatEventTime", () => {
  it("includes a clock time", () => {
    expect(formatEventTime("2026-09-29T05:17:00Z")).toMatch(/\d{1,2}:\d{2}/);
  });
});
