import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RiderTaskCard } from "./RiderTaskCard";
import type { MyTask } from "@/features/riders/types";

const useOutboxStatusMock = vi.fn();
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => useOutboxStatusMock(),
}));

const pickupTask: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Pickup", sequence: 1, awb: "AWB001",
  recipientName: "Recipient One", recipientPhone: "9800000000", recipientAddressLine: "New Road", recipientWardNumber: 5, codAmount: 0,
};
const deliveryTask: MyTask = { ...pickupTask, taskId: "t2", type: "Delivery", codAmount: 500 };

// RiderTaskCard calls useRiderPickupScan (a TanStack Query mutation) unconditionally, regardless
// of task type, so every render needs a QueryClientProvider ancestor — matching the convention in
// PickupScanControl.test.tsx for the sibling desktop component that wraps the same kind of hook.
function renderCard(task: MyTask, onRecordAttempt: (task: MyTask) => void) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RiderTaskCard task={task} riderId="r1" onRecordAttempt={onRecordAttempt} />
    </QueryClientProvider>
  );
}

describe("RiderTaskCard", () => {
  beforeEach(() => {
    useOutboxStatusMock.mockReturnValue({ pendingCount: 0, stuckItems: [], lastSyncedAt: null, refresh: vi.fn() });
  });

  it("shows recipient details and a COD badge when applicable", () => {
    renderCard(deliveryTask, vi.fn());
    expect(screen.getByText("Recipient One")).toBeInTheDocument();
    expect(screen.getByText(/Rs\. 500/)).toBeInTheDocument();
  });

  it("calls onRecordAttempt for a Delivery task", () => {
    const onRecordAttempt = vi.fn();
    renderCard(deliveryTask, onRecordAttempt);
    fireEvent.click(screen.getByRole("button", { name: /record attempt/i }));
    expect(onRecordAttempt).toHaveBeenCalledWith(deliveryTask);
  });

  it("does not show a Record Attempt button for a Pickup task", () => {
    renderCard(pickupTask, vi.fn());
    expect(screen.queryByRole("button", { name: /record attempt/i })).not.toBeInTheDocument();
  });

  it("shows a needs-review state instead of the Scan Pickup button when the outbox has a stuck item for this task's shipment", () => {
    useOutboxStatusMock.mockReturnValue({
      pendingCount: 0,
      stuckItems: [
        {
          id: "i1",
          action: { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" },
          idempotencyKey: "k1",
          createdAt: 0,
          status: "stuck",
          lastError: "wrong state",
        },
      ],
      lastSyncedAt: null,
      refresh: vi.fn(),
    });

    renderCard(pickupTask, vi.fn());

    expect(screen.getByText(/needs review/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /scan pickup/i })).not.toBeInTheDocument();
    expect(screen.queryByText("Picked up.")).not.toBeInTheDocument();
  });
});
