import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { MyToday } from "@/features/riders/types";
import type { OutboxItem } from "@/features/rider-app/sync/types";
import { updateOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
vi.mock("@/features/riders/hooks/useMyRider", () => ({
  useMyRider: () => ({ data: { id: "r1", licenseNumber: "LIC-0428812", roleType: "DeliveryRider" }, isLoading: false }),
}));
let today: { data?: MyToday; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/rider-app/hooks/useMyToday", () => ({ useMyToday: () => today }));
let permissions: string[] = [];
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { fullName: "Bikash Tamang", permissions } }) }));
let pendingCount = 0;
let stuckItems: OutboxItem[] = [];
const refresh = vi.fn();
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount, pendingItems: [], stuckItems, lastSyncedAt: null, refresh }),
}));
vi.mock("@/features/rider-app/sync/outboxDb", () => ({ updateOutboxItem: vi.fn().mockResolvedValue(undefined) }));
vi.mock("@/features/rider-app/sync/syncManager", () => ({ drainOutbox: vi.fn().mockResolvedValue({ synced: [], stuck: [] }) }));

import RiderProfilePage from "./page";

function day(overrides: Partial<MyToday> = {}): MyToday {
  return {
    run: { id: "run1", status: "Active" }, tasks: [
      { taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1, awb: "NX-260930-00148", recipientName: "Sita", recipientPhone: "1",
        recipientAddressLine: "A", recipientWardNumber: 1, codAmount: 2500, status: "Pending" },
    ],
    counts: { assigned: 32, completed: 14, failed: 2 },
    cash: { amount: 18450, parcelCount: 9, oldestCollectedAt: new Date(Date.now() - 3 * 3_600_000).toISOString() },
    branchName: "Lalitpur", vehiclePlate: "BA 2 PA 1111", ...overrides,
  };
}

beforeEach(() => {
  today = { data: day(), isLoading: false, isError: false, refetch: vi.fn() };
  permissions = ["delivery.attempt", "delivery.complete", "cod.collect", "shipment.scan"];
  pendingCount = 0;
  stuckItems = [];
  refresh.mockReset();
  push.mockReset();
  vi.mocked(updateOutboxItem).mockClear();
  vi.mocked(drainOutbox).mockClear();
});

describe("RiderProfilePage", () => {
  it("shows who the rider is, today's progress and cash in hand", () => {
    render(<RiderProfilePage />);
    expect(screen.getByRole("heading", { name: "Bikash Tamang" })).toBeInTheDocument();
    expect(screen.getByText("Delivery rider · Lalitpur")).toBeInTheDocument();
    const todayCard = within(screen.getByRole("region", { name: "Today" }));
    expect(todayCard.getByText("of 32 done")).toBeInTheDocument();
    expect(todayCard.getByText("2 failed")).toBeInTheDocument();
    expect(todayCard.getByText("Rs 18,450")).toBeInTheDocument();
    expect(todayCard.getByText("9 COD parcels · oldest 3 h")).toBeInTheDocument();
    expect(todayCard.getByText("Hand cash over at the Lalitpur branch COD counter before you sign off.")).toBeInTheDocument();
    expect(screen.getByText("BA 2 PA 1111")).toBeInTheDocument();
    expect(screen.getByText("LIC-0428812")).toBeInTheDocument();
  });

  it("never shows zeros while the day loads", () => {
    today = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    render(<RiderProfilePage />);
    expect(screen.queryByText(/of 0 done/)).toBeNull();
    expect(screen.getByRole("status", { name: "Loading today" })).toBeInTheDocument();
  });

  it("lists rejected actions with the AWB and lets the rider try again", async () => {
    stuckItems = [{
      id: "i1", action: { kind: "delivery-fail", riderId: "r1", shipmentId: "s1", input: { shipmentId: "s1", reason: "Other", customerContacted: false } },
      idempotencyKey: "k1", createdAt: 0, status: "stuck", lastError: "Shipment is not out for delivery",
    }];
    render(<RiderProfilePage />);
    const review = within(screen.getByRole("region", { name: "Needs review" }));
    expect(review.getByText("Needs review · 1")).toBeInTheDocument();
    expect(review.getByText("NX-260930-00148")).toBeInTheDocument();
    expect(review.getByText("Shipment is not out for delivery")).toBeInTheDocument();
    fireEvent.click(review.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(updateOutboxItem).toHaveBeenCalledWith("i1", { status: "pending", lastError: undefined }));
    expect(drainOutbox).toHaveBeenCalled();
  });

  it("shows the sync state and sends now on request", () => {
    pendingCount = 2;
    render(<RiderProfilePage />);
    const sync = within(screen.getByRole("region", { name: "Sync" }));
    expect(sync.getByText("2 waiting")).toBeInTheDocument();
    fireEvent.click(sync.getByRole("button", { name: "Send now" }));
    expect(drainOutbox).toHaveBeenCalled();
  });

  it("offers the staff console only to users with staff permissions, and signs out", async () => {
    const { unmount } = render(<RiderProfilePage />);
    expect(screen.queryByRole("link", { name: "Open staff console" })).toBeNull();
    unmount();

    permissions = ["shipment.scan", "manifest.read"];
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    render(<RiderProfilePage />);
    expect(screen.getByRole("link", { name: "Open staff console" })).toHaveAttribute("href", "/dashboard");
    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/login"));
  });
});
