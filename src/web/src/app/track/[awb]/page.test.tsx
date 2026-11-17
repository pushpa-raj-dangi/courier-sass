import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import TrackingResultPage from "./page";
import {
  useTrackShipment,
  TrackingNotFoundError,
  TrackingRateLimitedError,
} from "@/features/tracking/hooks/useTrackShipment";

vi.mock("next/navigation", () => ({
  useParams: () => ({ awb: "AWB123" }),
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock("@/features/tracking/hooks/useTrackShipment", async () => {
  const actual = await vi.importActual<typeof import("@/features/tracking/hooks/useTrackShipment")>(
    "@/features/tracking/hooks/useTrackShipment"
  );
  return { ...actual, useTrackShipment: vi.fn() };
});

function mockData(overrides: Record<string, unknown> = {}, extra: Record<string, unknown> = {}) {
  vi.mocked(useTrackShipment).mockReturnValue({
    isLoading: false,
    isError: false,
    dataUpdatedAt: Date.UTC(2026, 0, 2, 7, 13),
    data: {
      awb: "AWB123",
      currentStatus: "OutForDelivery",
      events: [
        { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: "Kathmandu" },
        { status: "OutForDelivery", timestamp: "2026-01-02T10:00:00Z", location: null },
      ],
      destinationArea: "Jawalakhel, Lalitpur",
      senderName: "Everest Organics",
      ...overrides,
    },
    error: null,
    ...extra,
  } as never);
}

describe("TrackingResultPage", () => {
  // Pin "now" to 30 minutes after mockData's OutForDelivery event so "Arriving today" (same
  // local day only) is deterministic. Only Date is faked.
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-01-02T10:30:00Z"));
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("says Out for delivery when the OFD event is from an earlier day", () => {
    vi.setSystemTime(new Date("2026-01-05T10:30:00Z"));
    mockData();
    render(<TrackingResultPage />);
    expect(screen.getByRole("heading", { level: 1, name: "Out for delivery" })).toBeInTheDocument();
    expect(screen.queryByText("Arriving today")).toBeNull();
  });

  it("shows the failed-attempt summary", () => {
    mockData({
      currentStatus: "DeliveryAttemptUnsuccessful",
      events: [
        { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: null },
        { status: "OutForDelivery", timestamp: "2026-01-02T08:00:00Z", location: null },
        { status: "DeliveryAttemptUnsuccessful", timestamp: "2026-01-02T10:00:00Z", location: null },
      ],
    });
    render(<TrackingResultPage />);
    expect(screen.getByRole("heading", { level: 1, name: "We missed you" })).toBeInTheDocument();
    expect(
      screen.getByText("A delivery attempt was unsuccessful. We'll contact you or try again.")
    ).toBeInTheDocument();
  });

  it("does not show a summary while out for delivery", () => {
    mockData();
    render(<TrackingResultPage />);
    expect(screen.queryByText("With a rider, on its way to the recipient.")).toBeNull();
  });

  it("shows a returning-to-seller headline and summary with no pending delivery", () => {
    mockData({
      currentStatus: "ReturningToSender",
      events: [
        { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: null },
        { status: "OutForDelivery", timestamp: "2026-01-02T08:00:00Z", location: null },
        { status: "DeliveryAttemptUnsuccessful", timestamp: "2026-01-02T09:00:00Z", location: null },
        { status: "ReturningToSender", timestamp: "2026-01-02T10:00:00Z", location: null },
      ],
    });
    render(<TrackingResultPage />);
    expect(screen.getByRole("heading", { level: 1, name: "Returning to the seller" })).toBeInTheDocument();
    expect(screen.getByText("This parcel is on its way back to the seller.")).toBeInTheDocument();
    expect(screen.getByText("Returning to Sender")).toBeInTheDocument();
    expect(screen.queryByText("We missed you")).toBeNull();
    expect(screen.queryByText(/^Delivered/)).toBeNull();
    expect(screen.queryByText(/upcoming/)).toBeNull();
  });

  it("skips the journey when there are no steps", () => {
    mockData({ currentStatus: "Cancelled", events: [] });
    render(<TrackingResultPage />);
    expect(screen.getByRole("heading", { level: 1, name: "Shipment cancelled" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Journey" })).toBeNull();
  });

  it("renders the generic error message as the single h1", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
      error: new Error("boom"),
    } as never);
    render(<TrackingResultPage />);
    const h1s = screen.getAllByRole("heading", { level: 1 });
    expect(h1s).toHaveLength(1);
    expect(h1s[0]).toHaveTextContent(/something went wrong/i);
  });

  it("keeps an accessible name on the back link and offers the app theme toggle", () => {
    mockData();
    render(<TrackingResultPage />);
    expect(screen.getByRole("link", { name: "Track another parcel" })).toHaveAttribute("href", "/track");
    expect(screen.getByRole("button", { name: /switch to (dark|light) mode|toggle theme/i })).toBeInTheDocument();
  });

  it("shows a loading skeleton while fetching", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: true,
      isError: false,
      data: undefined,
      error: null,
    } as never);

    render(<TrackingResultPage />);

    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
    expect(screen.getByRole("status", { name: /loading/i })).toBeInTheDocument();
  });

  it("renders headline, route line, journey and updated footer", () => {
    mockData();
    render(<TrackingResultPage />);
    expect(screen.getByRole("heading", { level: 1, name: "Arriving today" })).toBeInTheDocument();
    expect(screen.getByText("To Jawalakhel, Lalitpur · from Everest Organics")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Journey" })).toBeInTheDocument();
    expect(screen.getByText(/updated/i)).toBeInTheDocument();
  });

  it("omits the destination when null", () => {
    mockData({ destinationArea: null });
    render(<TrackingResultPage />);
    expect(screen.getByText(/from Everest Organics/)).toBeInTheDocument();
    expect(screen.queryByText(/To\s/)).toBeNull();
  });

  it("omits the route line when both parts are null", () => {
    mockData({ destinationArea: null, senderName: null });
    render(<TrackingResultPage />);
    expect(screen.queryByText(/^(To |from )/)).toBeNull();
    // No route line: the journey follows the headline directly.
    expect(screen.getByRole("heading", { level: 1 }).nextElementSibling?.tagName).toBe("SECTION");
  });

  it("shows the InTransit headline", () => {
    mockData({ currentStatus: "InTransit" });
    render(<TrackingResultPage />);
    expect(screen.getByRole("heading", { level: 1, name: "On its way" })).toBeInTheDocument();
    expect(screen.queryByText("Arriving today")).toBeNull();
  });

  it("renders data even when the query is in error", () => {
    mockData({}, { isError: true, error: new Error("boom") });
    render(<TrackingResultPage />);
    expect(screen.getByRole("heading", { level: 1, name: "Arriving today" })).toBeInTheDocument();
    expect(screen.queryByText(/something went wrong/i)).toBeNull();
  });

  it("has no out-of-scope copy", () => {
    mockData();
    render(<TrackingResultPage />);
    for (const t of [/OTP/, /stop/i, /Change delivery time/, /Contact support/]) {
      expect(screen.queryByText(t)).toBeNull();
    }
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
    const h1s = screen.getAllByRole("heading", { level: 1 });
    expect(h1s).toHaveLength(1);
    expect(h1s[0]).toHaveTextContent(/no shipment found/i);
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
    const h1s = screen.getAllByRole("heading", { level: 1 });
    expect(h1s).toHaveLength(1);
    expect(h1s[0]).toHaveTextContent(/try again in 42 seconds/i);
  });
});
