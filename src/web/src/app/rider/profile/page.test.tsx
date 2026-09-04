import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import RiderProfilePage from "./page";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));
vi.mock("@/features/riders/hooks/useMyRider", () => ({
  useMyRider: () => ({ data: { id: "r1", licenseNumber: "LIC-001", roleType: "DeliveryRider" }, isLoading: false }),
}));
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({
    pendingCount: 1,
    stuckItems: [{ id: "i1", action: { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" }, idempotencyKey: "k1", createdAt: 0, status: "stuck", lastError: "wrong state" }],
    lastSyncedAt: null,
    refresh: vi.fn(),
  }),
}));

describe("RiderProfilePage", () => {
  it("shows the rider's license number and any stuck queue items needing review", () => {
    render(<RiderProfilePage />);
    expect(screen.getByText("LIC-001")).toBeInTheDocument();
    expect(screen.getByText(/wrong state/)).toBeInTheDocument();
  });
});
