import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { TrackingTimeline } from "./TrackingTimeline";
import type { PublicTrackingEvent } from "@/features/tracking/types";

describe("TrackingTimeline", () => {
  it("renders one entry per event with its label and location", () => {
    const events: PublicTrackingEvent[] = [
      { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: "Kathmandu" },
      { status: "InTransit", timestamp: "2026-01-02T10:00:00Z", location: null },
    ];

    render(<TrackingTimeline events={events} />);

    expect(screen.getByText("Received")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu")).toBeInTheDocument();
    expect(screen.getByText("In Transit")).toBeInTheDocument();
  });
});
