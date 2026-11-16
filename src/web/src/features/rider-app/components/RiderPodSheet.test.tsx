import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useHandledStore } from "@/features/rider-app/store/handledStore";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import type { MyTask } from "@/features/riders/types";

const completeMutate = vi.fn();
const failMutate = vi.fn();
vi.mock("@/features/rider-app/hooks/useCompleteDelivery", () => ({
  useCompleteDelivery: () => ({ mutateAsync: completeMutate, isPending: false }),
}));
vi.mock("@/features/rider-app/hooks/useRecordFailedAttempt", () => ({
  useRecordFailedAttempt: () => ({ mutateAsync: failMutate, isPending: false }),
}));
const refresh = vi.fn();
vi.mock("@/features/rider-app/sync/outboxStatusStore", () => ({
  useOutboxStatusStore: { getState: () => ({ refresh }) },
}));
vi.mock("@/features/riders/components/SignaturePad", () => ({ SignaturePad: () => <div>signature pad</div> }));

import { RiderPodSheet } from "./RiderPodSheet";

const codTask: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 15, awb: "NX-260930-00148",
  recipientName: "Sita Shrestha", recipientPhone: "9800000412", recipientAddressLine: "Jawalakhel lane", recipientWardNumber: 4, codAmount: 2500,
};
const prepaidTask: MyTask = { ...codTask, taskId: "t2", shipmentId: "s2", codAmount: 0 };

beforeEach(() => {
  completeMutate.mockReset().mockResolvedValue(undefined);
  failMutate.mockReset().mockResolvedValue(undefined);
  refresh.mockReset();
  useRiderAppPanelStore.getState().close();
  useHandledStore.setState({ handled: new Set() });
});

describe("RiderPodSheet", () => {
  it("shows the stop, the steps in order and the Fonepay option as needing backend", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(codTask);
    render(<RiderPodSheet riderId="r1" />);
    expect(screen.getByText("Complete delivery")).toBeInTheDocument();
    expect(screen.getByText("NX-260930-00148 · stop 15")).toBeInTheDocument();
    expect(screen.getByText("1 · Collect COD")).toBeInTheDocument();
    expect(screen.getByText("2 · Delivery OTP")).toBeInTheDocument();
    expect(screen.getByText("3 · Proof")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Fonepay QR" })).toBeDisabled();
    expect(screen.getByText("Location unavailable")).toBeInTheDocument();
  });

  it("words the cash check and keeps Confirm disabled until name and cash are valid", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(codTask);
    render(<RiderPodSheet riderId="r1" />);
    const confirm = screen.getByRole("button", { name: /Confirm delivered/ });
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Received by"), { target: { value: "Sita Shrestha" } });
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2000" } });
    expect(screen.getByText("Short by Rs 500")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2600" } });
    expect(screen.getByText("Over by Rs 100")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "-5" } });
    expect(screen.getByRole("button", { name: /Confirm delivered/ })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    expect(screen.getByText("Matches")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm delivered · Rs 2,500 collected" })).toBeEnabled();
  });

  it("queues the delivery through the outbox hook and refreshes the sync status", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(codTask);
    render(<RiderPodSheet riderId="r1" />);
    fireEvent.change(screen.getByLabelText("Received by"), { target: { value: "Sita Shrestha" } });
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    fireEvent.click(screen.getByLabelText("Customer confirmed with OTP"));
    fireEvent.click(screen.getByRole("button", { name: "Confirm delivered · Rs 2,500 collected" }));

    await waitFor(() =>
      expect(completeMutate).toHaveBeenCalledWith(
        expect.objectContaining({ shipmentId: "s1", recipientName: "Sita Shrestha", otpVerified: true, collectedCodAmount: 2500, photoBlob: null }),
      ),
    );
    expect(refresh).toHaveBeenCalled();
    await waitFor(() => expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "closed" }));
    expect(useHandledStore.getState().handled.has("s1")).toBe(true);
  });

  it("does not mark a delivery handled when it couldn't be saved on the phone", async () => {
    completeMutate.mockRejectedValue(new Error("idb"));
    useRiderAppPanelStore.getState().openRecordAttempt(prepaidTask);
    render(<RiderPodSheet riderId="r1" />);
    fireEvent.change(screen.getByLabelText("Received by"), { target: { value: "Neighbour" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm delivered" }));
    expect(await screen.findByText("Couldn't save this delivery on the phone. Try again.")).toBeInTheDocument();
    expect(useHandledStore.getState().handled.has("s2")).toBe(false);
  });

  it("rejects a non-finite cash amount", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(codTask);
    render(<RiderPodSheet riderId="r1" />);
    fireEvent.change(screen.getByLabelText("Received by"), { target: { value: "Sita Shrestha" } });
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "Infinity" } });
    expect(screen.getByRole("button", { name: /Confirm delivered/ })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "1e999" } });
    expect(screen.getByRole("button", { name: /Confirm delivered/ })).toBeDisabled();
  });

  it("numbers the steps from 1 for a prepaid parcel and sends no COD amount", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(prepaidTask);
    render(<RiderPodSheet riderId="r1" />);
    expect(screen.queryByText(/Collect COD/)).toBeNull();
    expect(screen.getByText("1 · Delivery OTP")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Received by"), { target: { value: "Neighbour" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm delivered" }));
    await waitFor(() => expect(completeMutate).toHaveBeenCalledWith(expect.objectContaining({ collectedCodAmount: null })));
  });

  it("rejects a photo over 10MB and never sends it", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(prepaidTask);
    render(<RiderPodSheet riderId="r1" />);
    const big = new File([new Uint8Array(1)], "p.jpg", { type: "image/jpeg" });
    Object.defineProperty(big, "size", { value: 10 * 1024 * 1024 + 1 });
    fireEvent.change(screen.getByLabelText("Parcel photo"), { target: { files: [big] } });
    expect(await screen.findByText(/too large/i)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Received by"), { target: { value: "Neighbour" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm delivered" }));
    await waitFor(() => expect(completeMutate).toHaveBeenCalledWith(expect.objectContaining({ photoBlob: null })));
  });

  it("records a failed attempt with reason, contact and next action", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(codTask);
    render(<RiderPodSheet riderId="r1" />);
    fireEvent.click(screen.getByRole("button", { name: "Couldn't deliver" }));
    expect(screen.getByRole("button", { name: "Record failed attempt" })).toBeDisabled();

    fireEvent.click(screen.getByLabelText("Reason"));
    fireEvent.click(await screen.findByText("Customer unavailable"));
    fireEvent.click(screen.getByLabelText("Customer contacted"));
    fireEvent.change(screen.getByLabelText("Next action"), { target: { value: "Reattempt after 4 PM" } });
    fireEvent.click(screen.getByRole("button", { name: "Record failed attempt" }));

    await waitFor(() =>
      expect(failMutate).toHaveBeenCalledWith(
        expect.objectContaining({ shipmentId: "s1", reason: "CustomerUnavailable", customerContacted: true, nextAction: "Reattempt after 4 PM" }),
      ),
    );
    await waitFor(() => expect(useHandledStore.getState().handled.has("s1")).toBe(true));
  });

  it("caps notes and next action at the server's 500 characters", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(codTask, "fail");
    render(<RiderPodSheet riderId="r1" />);
    expect(screen.getByLabelText("Notes")).toHaveAttribute("maxLength", "500");
    expect(screen.getByLabelText("Next action")).toHaveAttribute("maxLength", "500");
  });

  it("opens straight to the failure form from Report issue, and can go back", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(codTask, "fail");
    render(<RiderPodSheet riderId="r1" />);
    expect(screen.getByRole("button", { name: "Record failed attempt" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Back to delivery" }));
    expect(screen.getByText("1 · Collect COD")).toBeInTheDocument();
  });
});
