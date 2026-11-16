import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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
vi.mock("@/features/rider-app/hooks/useRiderPickupScan", () => ({
  useRiderPickupScan: () => ({ mutateAsync: pickupMutate, isPending: false, isSuccess: false, isError: false }),
}));
const openRecordAttempt = vi.fn();
vi.mock("@/features/rider-app/store/riderAppPanelStore", () => ({
  useRiderAppPanelStore: (selector: (s: unknown) => unknown) => selector({ openRecordAttempt }),
}));

import RiderRunPage from "./page";
import { useHandledStore } from "@/features/rider-app/store/handledStore";

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

function deliveryOutbox(shipmentId: string, status: OutboxItem["status"]): OutboxItem {
  return {
    id: `d-${shipmentId}`,
    action: {
      kind: "delivery-complete", riderId: "r1", shipmentId, recipientName: "Sita", otpVerified: false, latitude: null, longitude: null,
      collectedCodAmount: 2500, signatureBlob: null, photoBlob: null, signatureStorageKey: null, photoStorageKey: null,
    },
    idempotencyKey: "k", createdAt: 0, status,
  };
}

beforeEach(() => {
  riderQuery = { data: { id: "r1" }, isLoading: false, isError: false };
  todayQuery = { data: day(), isLoading: false, isError: false, refetch: vi.fn() };
  pendingItems = [];
  stuckItems = [];
  pickupMutate.mockReset();
  pickupMutate.mockResolvedValue(undefined);
  useHandledStore.setState({ handled: new Set() });
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

  it("shows a pickup waiting to sync as a row, never as a scannable next stop", () => {
    pendingItems = [outbox("s3", "pending")];
    render(<RiderRunPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Pickups · 1" }));
    expect(screen.queryByRole("article", { name: "Next stop: Hari Traders" })).toBeNull();
    expect(screen.getByText("Saved · waiting to sync")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Scan pickup" })).toBeNull();
  });

  it("enqueues a pickup scan, refreshes the sync status and moves the pickup out of the next-stop slot", async () => {
    render(<RiderRunPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Pickups · 1" }));
    fireEvent.click(screen.getByRole("button", { name: "Scan pickup" }));
    expect(pickupMutate).toHaveBeenCalledWith({ awb: "NX-260930-00152", shipmentId: "s3" });
    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(screen.queryByRole("article", { name: "Next stop: Hari Traders" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Scan pickup" })).toBeNull();
    expect(screen.getByText("Saved · waiting to sync")).toBeInTheDocument();
  });

  it("skips a delivery already saved on this phone for the next stop and Report issue", () => {
    pendingItems = [deliveryOutbox("s1", "pending")];
    render(<RiderRunPage />);
    expect(screen.queryByRole("article", { name: "Next stop: Sita Shrestha" })).toBeNull();
    const card = within(screen.getByRole("article", { name: "Next stop: Ramesh Thapa" }));
    expect(card.getByRole("button", { name: "Deliver" })).toBeInTheDocument();
    // The handled delivery is a plain row: its sync note, no button to open it again.
    expect(screen.getByText("Saved · waiting to sync")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Sita Shrestha/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Report issue" }));
    expect(openRecordAttempt).toHaveBeenCalledTimes(1);
    expect(openRecordAttempt).toHaveBeenCalledWith(expect.objectContaining({ taskId: "t2" }), "fail");
  });

  it("keeps a delivery handled this session out of reach after the outbox drains, until the run shows it done", () => {
    useHandledStore.getState().markHandled("s1");
    const { rerender } = render(<RiderRunPage />);
    expect(screen.queryByRole("article", { name: "Next stop: Sita Shrestha" })).toBeNull();
    expect(screen.getByRole("article", { name: "Next stop: Ramesh Thapa" })).toBeInTheDocument();
    expect(screen.getByText("Saved · waiting to sync")).toBeInTheDocument();
    expect(useHandledStore.getState().handled.has("s1")).toBe(true);

    todayQuery = {
      ...todayQuery,
      data: day({ tasks: TASKS.map((t) => (t.shipmentId === "s1" ? { ...t, status: "Completed" as const } : t)) }),
    };
    rerender(<RiderRunPage />);
    expect(useHandledStore.getState().handled.has("s1")).toBe(false);
  });

  it("shows a rejected delivery as needing review with nothing to press, and disables Report issue when nothing is left", () => {
    stuckItems = [deliveryOutbox("s1", "stuck")];
    pendingItems = [deliveryOutbox("s2", "pending")];
    render(<RiderRunPage />);
    expect(screen.queryByRole("article")).toBeNull();
    expect(screen.queryByRole("button", { name: "Deliver" })).toBeNull();
    expect(screen.getByText("Needs review — see Profile")).toBeInTheDocument();
    expect(screen.getByText("Saved · waiting to sync")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Report issue" })).toBeDisabled();
  });

  it("keeps the run on screen when a background refresh fails", () => {
    todayQuery = { data: day(), isLoading: false, isError: true, refetch: vi.fn() };
    render(<RiderRunPage />);
    expect(screen.getByRole("article", { name: "Next stop: Sita Shrestha" })).toBeInTheDocument();
    expect(screen.getByText("Couldn't refresh · showing what was last loaded")).toBeInTheDocument();
    expect(screen.queryByText("Couldn't load today's run")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(todayQuery.refetch).toHaveBeenCalled();
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
