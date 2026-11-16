import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { MyToday, MyTodayTask } from "@/features/riders/types";
import type { OutboxItem } from "@/features/rider-app/sync/types";

vi.mock("@/components/scanner/BarcodeScanner", () => ({
  BarcodeScanner: ({ onDetect }: { onDetect: (awb: string) => void }) => (
    <div>
      <button type="button" onClick={() => onDetect("NX-260930-00152")}>scan pickup</button>
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
vi.mock("@/features/rider-app/hooks/useRiderPickupScan", () => ({ useRiderPickupScan: () => ({ mutate: pickupMutate, isPending: false }) }));
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
  openRecordAttempt.mockReset();
  refresh.mockReset();
});

describe("RiderScanPage", () => {
  it("shows the branch and what is left on the run", () => {
    render(<RiderScanPage />);
    expect(screen.getByRole("heading", { name: "Scan parcel" })).toBeInTheDocument();
    expect(screen.getByText("Lalitpur · 1 to deliver · 1 pickup")).toBeInTheDocument();
  });

  it("runs the pickup scan for a pickup AWB and shows it saved on the phone", () => {
    pickupMutate.mockImplementation((_vars, options) => {
      pendingItems = [{ id: "o1", action: { kind: "pickup-scan", riderId: "r1", shipmentId: "s3", awb: "NX-260930-00152" }, idempotencyKey: "k", createdAt: 0, status: "pending" }];
      options?.onSuccess?.();
    });
    const { rerender } = render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan pickup" }));
    expect(pickupMutate).toHaveBeenCalledWith({ awb: "NX-260930-00152", shipmentId: "s3" }, expect.anything());
    expect(refresh).toHaveBeenCalled();
    rerender(<RiderScanPage />);
    const result = within(screen.getByRole("region", { name: "Last scan" }));
    expect(result.getByText("PICKED UP")).toBeInTheDocument();
    expect(result.getByText("Pickup from Hari Traders, Kupondole")).toBeInTheDocument();
    expect(result.getByText("Saved on this phone · waiting to sync")).toBeInTheDocument();
    expect(screen.getByText("1 scan waiting to sync")).toBeInTheDocument();
  });

  it("opens the POD Sheet for a delivery AWB", () => {
    render(<RiderScanPage />);
    fireEvent.click(screen.getByRole("button", { name: "scan delivery" }));
    expect(openRecordAttempt).toHaveBeenCalledWith(expect.objectContaining({ taskId: "t1" }));
    expect(within(screen.getByRole("region", { name: "Last scan" })).getByText("OPENED DELIVERY")).toBeInTheDocument();
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
