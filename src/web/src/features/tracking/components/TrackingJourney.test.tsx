import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { TrackingJourney } from "./TrackingJourney";
import { buildJourney } from "@/features/tracking/lib/journey";
import type { PublicTrackingResult } from "@/features/tracking/types";

const base: PublicTrackingResult = {
  awb: "AWB123",
  currentStatus: "Received",
  events: [{ status: "Received", timestamp: "2026-01-01T10:00:00Z", location: "Kathmandu" }],
  destinationArea: null,
  senderName: null,
};

describe("TrackingJourney", () => {
  it("renders six steps with now then next states", () => {
    render(<TrackingJourney steps={buildJourney(base)} />);
    const items = screen.getByRole("region", { name: "Journey" }).querySelectorAll("li");
    expect(items).toHaveLength(6);
    expect(Array.from(items).map((li) => li.getAttribute("data-state"))).toEqual([
      "now",
      "next",
      "next",
      "next",
      "next",
      "next",
    ]);
  });

  it("shows no time for a next step", () => {
    render(<TrackingJourney steps={buildJourney(base)} />);
    const items = screen.getByRole("region", { name: "Journey" }).querySelectorAll("li");
    expect(items[1].textContent).toBe("Picked up from seller (upcoming)");
  });

  it("marks only the current step with aria-current and exposes each state word", () => {
    const inTransit: PublicTrackingResult = {
      ...base,
      currentStatus: "InTransit",
      events: [
        { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: null },
        { status: "PickedUp", timestamp: "2026-01-01T12:00:00Z", location: null },
        { status: "InTransit", timestamp: "2026-01-02T10:00:00Z", location: null },
      ],
    };
    render(<TrackingJourney steps={buildJourney(inTransit)} />);
    const items = Array.from(screen.getByRole("region", { name: "Journey" }).querySelectorAll("li"));
    const word: Record<string, string> = { done: "completed", now: "current", next: "upcoming" };
    for (const li of items) {
      const state = li.getAttribute("data-state") as string;
      expect(li.textContent).toContain(word[state]);
      expect(li.getAttribute("aria-current")).toBe(state === "now" ? "step" : null);
    }
    expect(items.filter((li) => li.hasAttribute("aria-current"))).toHaveLength(1);
  });

  it("shows the alert label for a failed attempt", () => {
    const failed: PublicTrackingResult = {
      ...base,
      currentStatus: "DeliveryAttemptUnsuccessful",
      events: [
        { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: null },
        { status: "DeliveryAttemptUnsuccessful", timestamp: "2026-01-03T10:00:00Z", location: null },
      ],
    };
    render(<TrackingJourney steps={buildJourney(failed)} />);
    expect(screen.getByText("Delivery attempt unsuccessful", { exact: false })).toBeVisible();
    const alertItem = screen.getByText("Delivery attempt unsuccessful", { exact: false }).closest("li");
    expect(alertItem?.getAttribute("data-state")).toBe("now");
    expect(alertItem?.getAttribute("aria-current")).toBe("step");
    expect(alertItem?.textContent).toContain("current");
  });
});
