import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import { RiderRecordAttemptSheet } from "./RiderRecordAttemptSheet";
import type { MyTask } from "@/features/riders/types";

const failMutate = vi.fn();
const completeMutate = vi.fn();
vi.mock("@/features/rider-app/hooks/useRecordFailedAttempt", () => ({
  useRecordFailedAttempt: () => ({ mutateAsync: failMutate, isPending: false }),
}));
vi.mock("@/features/rider-app/hooks/useCompleteDelivery", () => ({
  useCompleteDelivery: () => ({ mutateAsync: completeMutate, isPending: false }),
}));

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1, awb: "AWB001",
  recipientName: "Recipient One", recipientPhone: "9800000000", recipientAddressLine: "New Road", recipientWardNumber: 5, codAmount: 0,
};

function renderSheet() {
  const queryClient = new QueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <RiderRecordAttemptSheet riderId="r1" />
    </QueryClientProvider>
  );
}

describe("RiderRecordAttemptSheet", () => {
  beforeEach(() => {
    failMutate.mockReset().mockResolvedValue({});
    completeMutate.mockReset().mockResolvedValue({});
    useRiderAppPanelStore.getState().close();
  });

  it("submits a failure attempt with the selected reason", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    renderSheet();

    fireEvent.click(screen.getByRole("button", { name: "Failed" }));
    fireEvent.click(screen.getByLabelText("Reason"));
    fireEvent.click(await screen.findByText("CustomerUnavailable"));
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() =>
      expect(failMutate).toHaveBeenCalledWith(
        expect.objectContaining({ shipmentId: "s1", reason: "CustomerUnavailable" })
      )
    );
  });

  it("submits a successful delivery with recipient name and blobs", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    renderSheet();

    fireEvent.change(screen.getByLabelText("Recipient Name"), { target: { value: "Recipient One" } });
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() =>
      expect(completeMutate).toHaveBeenCalledWith(
        expect.objectContaining({ shipmentId: "s1", recipientName: "Recipient One" })
      )
    );
  });

  it("rejects a photo over 10MB with an inline error and does not carry it as the photo blob on submit", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    renderSheet();

    const oversizedFile = new File([new Uint8Array(1)], "photo.jpg", { type: "image/jpeg" });
    Object.defineProperty(oversizedFile, "size", { value: 10 * 1024 * 1024 + 1 });

    fireEvent.change(screen.getByLabelText("Photo"), { target: { files: [oversizedFile] } });

    expect(await screen.findByText(/too large/i)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Recipient Name"), { target: { value: "Recipient One" } });
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() =>
      expect(completeMutate).toHaveBeenCalledWith(
        expect.objectContaining({ shipmentId: "s1", photoBlob: null })
      )
    );
  });
});
