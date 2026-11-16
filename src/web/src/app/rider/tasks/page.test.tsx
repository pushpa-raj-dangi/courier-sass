import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { MyToday, MyTodayTask } from "@/features/riders/types";
import type { OutboxItem } from "@/features/rider-app/sync/types";

vi.mock("@/features/riders/hooks/useMyRider", () => ({ useMyRider: () => riderQuery }));
let riderQuery: { data?: unknown; isLoading: boolean; isError: boolean };
let todayQuery: { data?: MyToday; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/rider-app/hooks/useMyToday", () => ({ useMyToday: () => todayQuery }));
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { fullName: "Bikash Tamang", permissions: [] } }) }));
let pendingItems: OutboxItem[] = [];
let stuckItems: OutboxItem[] = [];
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount: pendingItems.length, pendingItems, stuckItems, lastSyncedAt: null, refresh: vi.fn() }),
}));
const refresh = vi.fn();
vi.mock("@/features/rider-app/sync/outboxStatusStore", () => ({ useOutboxStatusStore: { getState: () => ({ refresh }) } }));
const pickupMutate = vi.fn();
vi.mock("@/features/rider-app/hooks/useRiderPickupScan", () => ({ useRiderPickupScan: () => ({ mutate: pickupMutate, isPending: false }) }));
const openRecordAttempt = vi.fn();
vi.mock("@/features/rider-app/store/riderAppPanelStore", () => ({
  useRiderAppPanelStore: (selector: (s: unknown) => unknown) => selector({ openRecordAttempt }),
}));

import RiderRunPage from "./page";

function task(overrides: Partial<MyTodayTask>): MyTodayTask {
  return {
    taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 15, awb: "NX-260930-00148", recipientName: "Sita Shrestha",
    recipientPhone: "9800000412", recipientAddressLine: "Jawalakhel lane", recipientWardNumber: 4, codAmount: 2500, status: "Pending",
    ...overrides,
  };
}

const TASKS = [
  task({}),
  task({ taskId: "t2", shipmentId: "s2", sequence: 16, recipientName: "Ramesh Thapa", recipientAddressLine: "Pulchowk", codAmount: 0, awb: "NX-260930-00151" }),
  task({ taskId: "t3", shipmentId: "s3", sequence: 3, type: "Pickup", recipientName: "Hari Traders", recipientAddressLine: "Kupondole", awb: "NX-260930-00152", codAmount: 0 }),
  task({ taskId: "t4", shipmentId: "s4", sequence: 1, status: "Completed", recipientName: "Done Person", awb: "NX-260930-00101" }),
  task({ taskId: "t5", shipmentId: "s5", sequence: 2, status: "Failed", recipientName: "Failed Person", awb: "NX-260930-00102" }),
];

function day(overrides: Partial<MyToday> = {}): MyToday {
  return {
    run: { id: "run1", status: "Active" }, tasks: TASKS, counts: { assigned: 5, completed: 1, failed: 1 },
    cash: { amount: 18450, parcelCount: 9, oldestCollectedAt: "2026-11-16T05:00:00Z" }, branchName: "Lalitpur", vehiclePlate: "BA 2 PA 1111",
    ...overrides,
  };
}

function outbox(shipmentId: string, status: OutboxItem["status"]): OutboxItem {
  return { id: `o-${shipmentId}`, action: { kind: "pickup-scan", riderId: "r1", shipmentId, awb: "A" }, idempotencyKey: "k", createdAt: 0, status };
}

beforeEach(() => {
  riderQuery = { data: { id: "r1" }, isLoading: false, isError: false };
  todayQuery = { data: day(), isLoading: false, isError: false, refetch: vi.fn() };
  pendingItems = [];
  stuckItems = [];
  pickupMutate.mockReset();
  openRecordAttempt.mockReset();
  refresh.mockReset();
});

describe("RiderRunPage", () => {
  it("greets the rider and shows done/total and cash in hand", () => {
    render(<RiderRunPage />);
    expect(screen.getByRole("heading", { name: "Namaste, Bikash" })).toBeInTheDocument();
    expect(screen.getByText("Run · Lalitpur")).toBeInTheDocument();
    expect(screen.getByText("/ 5")).toBeInTheDocument();
    expect(screen.getByText("Rs 18,450")).toBeInTheDocument();
    expect(screen.getByText("from 9 COD parcels")).toBeInTheDocument();
  });

  it("never shows zeros while the day loads, and offers retry on error", () => {
    todayQuery = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = render(<RiderRunPage />);
    expect(screen.queryByText("/ 0")).toBeNull();
    expect(screen.getAllByRole("status").length).toBeGreaterThan(0);
    unmount();

    todayQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<RiderRunPage />);
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(todayQuery.refetch).toHaveBeenCalled();
  });

  it("leads the To deliver tab with the next stop and its actions", () => {
    render(<RiderRunPage />);
    expect(screen.getByRole("tab", { name: "To deliver · 2" })).toHaveAttribute("aria-selected", "true");
    const card = within(screen.getByRole("article", { name: "Next stop: Sita Shrestha" }));
    expect(card.getByText("COD Rs 2,500")).toBeInTheDocument();
    expect(card.getByText("…00148")).toBeInTheDocument();
    expect(card.getByRole("link", { name: /Call/ })).toHaveAttribute("href", "tel:9800000412");
    expect(card.getByRole("link", { name: /Map/ })).toHaveAttribute("href", expect.stringContaining("google.com/maps/search"));
    fireEvent.click(card.getByRole("button", { name: "Deliver" }));
    expect(openRecordAttempt).toHaveBeenCalledWith(expect.objectContaining({ taskId: "t1" }));
    expect(screen.getByText("Prepaid")).toBeInTheDocument();
  });

  it("runs the pickup scan from the Pickups tab and shows it waiting to sync", () => {
    pendingItems = [outbox("s3", "pending")];
    render(<RiderRunPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Pickups · 1" }));
    const card = within(screen.getByRole("article", { name: "Next stop: Hari Traders" }));
    expect(card.getByText("Saved · waiting to sync")).toBeInTheDocument();
    expect(card.getByRole("button", { name: "Scan pickup" })).toBeDisabled();
  });

  it("enqueues a pickup scan and refreshes the sync status", () => {
    pickupMutate.mockImplementation((_vars, options) => options?.onSuccess?.());
    render(<RiderRunPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Pickups · 1" }));
    fireEvent.click(screen.getByRole("button", { name: "Scan pickup" }));
    expect(pickupMutate).toHaveBeenCalledWith({ awb: "NX-260930-00152", shipmentId: "s3" }, expect.anything());
    expect(refresh).toHaveBeenCalled();
  });

  it("lists done tasks with their result in words and flags rejected ones", () => {
    stuckItems = [outbox("s5", "stuck")];
    render(<RiderRunPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Done · 2" }));
    expect(screen.getByText("Delivered")).toBeInTheDocument();
    expect(screen.getByText("Failed")).toBeInTheDocument();
    expect(screen.getByText("Needs review — see Profile")).toBeInTheDocument();
  });

  it("opens the failure form for the next stop from Report issue", () => {
    render(<RiderRunPage />);
    fireEvent.click(screen.getByRole("button", { name: "Report issue" }));
    expect(openRecordAttempt).toHaveBeenCalledWith(expect.objectContaining({ taskId: "t1" }), "fail");
    expect(screen.getByRole("link", { name: /Scan parcel/ })).toHaveAttribute("href", "/rider/scan");
  });

  it("says when there is no run today", () => {
    todayQuery = { data: day({ run: null, tasks: [], counts: { assigned: 0, completed: 0, failed: 0 }, cash: null }), isLoading: false, isError: false, refetch: vi.fn() };
    render(<RiderRunPage />);
    expect(screen.getAllByText("No run today").length).toBeGreaterThan(0);
    expect(screen.getByText("No cash held")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Report issue" })).toBeDisabled();
  });

  it("shows the no-rider state for an account without a rider", () => {
    riderQuery = { data: undefined, isLoading: false, isError: true };
    render(<RiderRunPage />);
    expect(screen.getByText("No rider profile")).toBeInTheDocument();
  });
});
