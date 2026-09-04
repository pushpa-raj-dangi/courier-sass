import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RiderTaskCard } from "./RiderTaskCard";
import type { MyTask } from "@/features/riders/types";

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
});
