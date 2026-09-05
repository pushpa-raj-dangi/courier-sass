import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import RiderProfilePage from "./page";
import { updateOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));
vi.mock("@/features/riders/hooks/useMyRider", () => ({
  useMyRider: () => ({ data: { id: "r1", licenseNumber: "LIC-001", roleType: "DeliveryRider" }, isLoading: false }),
}));
const refreshMock = vi.fn();
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({
    pendingCount: 1,
    stuckItems: [{ id: "i1", action: { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" }, idempotencyKey: "k1", createdAt: 0, status: "stuck", lastError: "wrong state" }],
    lastSyncedAt: null,
    refresh: refreshMock,
  }),
}));
vi.mock("@/features/rider-app/sync/outboxDb", () => ({
  updateOutboxItem: vi.fn().mockResolvedValue(undefined),
}));
vi.mock("@/features/rider-app/sync/syncManager", () => ({
  drainOutbox: vi.fn().mockResolvedValue({ synced: [], stuck: [] }),
}));

describe("RiderProfilePage", () => {
  beforeEach(() => {
    vi.mocked(updateOutboxItem).mockClear();
    vi.mocked(drainOutbox).mockClear();
    refreshMock.mockClear();
  });

  it("shows the rider's license number and any stuck queue items needing review", () => {
    render(<RiderProfilePage />);
    expect(screen.getByText("LIC-001")).toBeInTheDocument();
    expect(screen.getByText(/wrong state/)).toBeInTheDocument();
  });

  it("shows a link back to the staff dashboard", () => {
    render(<RiderProfilePage />);
    const link = screen.getByRole("link", { name: /back to staff app/i });
    expect(link).toHaveAttribute("href", "/dashboard");
  });

  it("clicking Retry on a stuck item flips it back to pending and kicks a drain", async () => {
    render(<RiderProfilePage />);

    fireEvent.click(screen.getByRole("button", { name: /retry/i }));

    await waitFor(() =>
      expect(updateOutboxItem).toHaveBeenCalledWith("i1", { status: "pending", lastError: undefined })
    );
    expect(drainOutbox).toHaveBeenCalled();
  });
});
