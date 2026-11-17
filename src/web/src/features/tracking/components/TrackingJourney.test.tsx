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
    expect(items[1].textContent).toBe("Picked up from seller");
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
    expect(screen.getByText("Delivery attempt unsuccessful")).toBeVisible();
  });
});
