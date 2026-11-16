import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { MyToday, MyTodayTask } from "@/features/riders/types";
import type { OutboxItem } from "@/features/rider-app/sync/types";

vi.mock("@/components/scanner/BarcodeScanner", () => ({
  BarcodeScanner: ({ onDetect }: { onDetect: (awb: string) => void }) => (
    <div>
      <button type="button" onClick={() => onDetect("NX-260930-00152")}>scan pickup</button>
      <button type="button" onClick={() => onDetect("NX-260930-00153")}>scan pickup 2</button>
      <button type="button" onClick={() => onDetect("NX-260930-00148")}>scan delivery</button>
      <button type="button" onClick={() => onDetect("NX-260930-00101")}>scan done</button>
      <button type="button" onClick={() => onDetect("NX-260930-00199")}>scan unknown</button>
    </div>
  ),
}));
vi.mock("@/features/riders/hooks/useMyRider", () => ({ useMyRider: () => ({ data: { id: "r1" } }) }));
let today: MyToday | undefined;
vi.mock("@/features/rider-app/hooks/useMyToday", () => ({ useMyToday: () => ({ data: today, isLoading: !today, isError: false }) }));
let pendingItems: OutboxItem[] = [];
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount: pendingItems.length, pendingItems, stuckItems: [], lastSyncedAt: null, refresh: vi.fn() }),
}));
const refresh = vi.fn();
vi.mock("@/features/rider-app/sync/outboxStatusStore", () => ({ useOutboxStatusStore: { getState: () => ({ refresh }) } }));
const pickupMutate = vi.fn();
vi.mock("@/features/rider-app/hooks/useRiderPickupScan", () => ({ useRiderPickupScan: () => ({ mutateAsync: pickupMutate, isPending: false }) }));
const openRecordAttempt = vi.fn();
vi.mock("@/features/rider-app/store/riderAppPanelStore", () => ({
  useRiderAppPanelStore: (selector: (s: unknown) => unknown) => selector({ openRecordAttempt }),
}));

import RiderScanPage from "./page";

function task(overrides: Partial<MyTodayTask>): MyTodayTask {
  return {
    taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 15, awb: "NX-260930-00148", recipientName: "Sita Shrestha",
    recipientPhone: "9800000412", recipientAddressLine: "Jawalakhel lane", recipientWardNumber: 4, codAmount: 2500, status: "Pending",
    ...overrides,
  };
}

beforeEach(() => {
  today = {
    run: { id: "run1", status: "Active" },
    tasks: [
      task({}),
      task({ taskId: "t3", shipmentId: "s3", type: "Pickup", recipientName: "Hari Traders", recipientAddressLine: "Kupondole", awb: "NX-260930-00152", codAmount: 0 }),
      task({ taskId: "t4", shipmentId: "s4", status: "Completed", awb: "NX-260930-00101" }),
    ],
    counts: { assigned: 3, completed: 1, failed: 0 }, cash: null, branchName: "Lalitpur", vehiclePlate: null,
  };
  pendingItems = [];
  pickupMutate.mockReset();
  pickupMutate.mockResolvedValue(undefined);
  openRecordAttempt.mockReset();
  refresh.mockReset();
});

describe("RiderScanPage", () => {
  it("shows the branch and what is left on the run", () => {
    render(<RiderScanPage />);
    expect(screen.getByRole("heading", { name: "Scan parcel" })).toBeInTheDocument();
    expect(screen.getByText("Lalitpur · 1 to deliver · 1 pickup")).toBeInTheDocument();
  });

  it("runs the pickup scan for a pickup AWB and shows it saved on the phone", async () => {
    pickupMutate.mockImplementation(async () => {
      pendingItems = [{ id: "o1", action: { kind: "pickup-scan", riderId: "r1", shipmentId: "s3", awb: "NX-260930-00152" }, idempotencyKey: "k", createdAt: 0, status: "pending" }];
    });
    const { rerender } = render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    expect(pickupMutate).toHaveBeenCalledWith({ awb: "NX-260930-00152", shipmentId: "s3" });
    await waitFor(() => expect(refresh).toHaveBeenCalled());
    rerender(<RiderScanPage />);
    const result = within(screen.getByRole("status", { name: "Last scan" }));
    expect(result.getByText("PICKED UP")).toBeInTheDocument();
    expect(result.getByText("Pickup from Hari Traders, Kupondole")).toBeInTheDocument();
    expect(result.getByText("Saved on this phone · waiting to sync")).toBeInTheDocument();
    expect(screen.getByText("1 scan waiting to sync")).toBeInTheDocument();
  });

  it("does not classify scans while the run has not loaded", () => {
    today = undefined;
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    expect(screen.getByText("Your run is still loading — scan again in a moment.")).toBeInTheDocument();
    expect(pickupMutate).not.toHaveBeenCalled();
    expect(openRecordAttempt).not.toHaveBeenCalled();
    expect(screen.queryByText(/isn't on your run/)).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Recent scans" })).not.toBeInTheDocument();
  });

  it("never shows Synced when the pickup could not be saved", async () => {
    pickupMutate.mockRejectedValue(new Error("nope"));
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    expect(await within(screen.getByRole("status", { name: "Last scan" })).findByText("Couldn't save this scan · scan again")).toBeInTheDocument();
    expect(within(screen.getByRole("status", { name: "Last scan" })).getByText("NOT SAVED")).toBeInTheDocument();
    expect(screen.queryByText("PICKED UP")).not.toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Recent scans" })).getByText("Couldn't save · scan again")).toBeInTheDocument();
    expect(screen.queryByText("Synced")).not.toBeInTheDocument();
  });

  it("does not enqueue a pickup that is already waiting to sync", () => {
    pendingItems = [{ id: "o1", action: { kind: "pickup-scan", riderId: "r1", shipmentId: "s3", awb: "NX-260930-00152" }, idempotencyKey: "k", createdAt: 0, status: "pending" }];
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    expect(pickupMutate).not.toHaveBeenCalled();
    expect(within(screen.getByRole("status", { name: "Last scan" })).getByText("Saved on this phone · waiting to sync")).toBeInTheDocument();
  });

  it("settles every scan when two pickups are scanned back to back", async () => {
    today!.tasks.push(task({ taskId: "t5", shipmentId: "s5", type: "Pickup", recipientName: "Gita Store", recipientAddressLine: "Patan", awb: "NX-260930-00153", codAmount: 0 }));
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    fireEvent.click(screen.getByRole("button", { name: "scan pickup 2" }));
    expect(pickupMutate).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(screen.queryByText("Saving…")).not.toBeInTheDocument());
    expect(refresh).toHaveBeenCalledTimes(2);
    expect(screen.getAllByText("Waiting to sync")).toHaveLength(2);
  });

  it("does not enqueue the same pickup again while the first scan is still saving", () => {
    pickupMutate.mockReturnValue(new Promise(() => {}));
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    expect(pickupMutate).toHaveBeenCalledTimes(1);
  });

  it("opens the POD Sheet for a delivery AWB", () => {
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan delivery" }));
    expect(openRecordAttempt).toHaveBeenCalledWith(expect.objectContaining({ taskId: "t1" }));
    expect(within(screen.getByRole("status", { name: "Last scan" })).getByText("OPENED DELIVERY")).toBeInTheDocument();
  });

  it("warns for an AWB that is already done or not on the run, and writes nothing", () => {
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan done" }));
    expect(screen.getByText("NX-260930-00101 is already done today")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "scan unknown" }));
    expect(screen.getByText("NX-260930-00199 isn't on your run today")).toBeInTheDocument();
    expect(pickupMutate).not.toHaveBeenCalled();
    expect(openRecordAttempt).not.toHaveBeenCalled();
    const recent = within(screen.getByRole("region", { name: "Recent scans" }));
    expect(recent.getAllByText("Not on run")).toHaveLength(1);
    expect(recent.getByText("Already done")).toBeInTheDocument();
  });
});
