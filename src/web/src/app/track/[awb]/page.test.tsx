import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import TrackingResultPage from "./page";
import {
  useTrackShipment,
  TrackingNotFoundError,
  TrackingRateLimitedError,
} from "@/features/tracking/hooks/useTrackShipment";

vi.mock("next/navigation", () => ({
  useParams: () => ({ awb: "AWB123" }),
}));

vi.mock("@/features/tracking/hooks/useTrackShipment", async () => {
  const actual = await vi.importActual<typeof import("@/features/tracking/hooks/useTrackShipment")>(
    "@/features/tracking/hooks/useTrackShipment"
  );
  return { ...actual, useTrackShipment: vi.fn() };
});

describe("TrackingResultPage", () => {
  it("shows a loading skeleton while fetching", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: true,
      isError: false,
      data: undefined,
      error: null,
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getByText(/AWB123/)).toBeInTheDocument();
  });

  it("renders the current status and timeline on success", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: false,
      isError: false,
      data: {
        awb: "AWB123",
        currentStatus: "InTransit",
        events: [
          { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: "Kathmandu" },
          { status: "InTransit", timestamp: "2026-01-02T10:00:00Z", location: null },
        ],
      },
      error: null,
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getAllByText("In Transit").length).toBeGreaterThan(0);
    expect(screen.getByText("Kathmandu")).toBeInTheDocument();
  });

  it("shows a not-found message for an unknown AWB", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
      error: new TrackingNotFoundError(),
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getByText(/no shipment found/i)).toBeInTheDocument();
  });

  it("shows a rate-limited message with the retry time", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
      error: new TrackingRateLimitedError(42),
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getByText(/try again in 42 seconds/i)).toBeInTheDocument();
  });
});
