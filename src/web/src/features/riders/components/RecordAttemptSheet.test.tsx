import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RecordAttemptSheet } from "./RecordAttemptSheet";
import { useMyDeliveriesPanelStore } from "@/features/riders/store/myDeliveriesPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { MyTask } from "@/features/riders/types";

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1,
  awb: "AWB001", recipientName: "Recipient One", recipientPhone: "9800000000",
  recipientAddressLine: "Address", recipientWardNumber: 1, codAmount: 0,
};

function renderWithProviders(riderId = "r1") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RecordAttemptSheet riderId={riderId} />
    </QueryClientProvider>
  );
}

describe("RecordAttemptSheet", () => {
  it("is not visible when the store is closed", () => {
    useMyDeliveriesPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("Record Delivery Attempt")).not.toBeInTheDocument();
  });

  it("defaults to the success branch and shows POD fields", () => {
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    expect(screen.getByText("Record Delivery Attempt")).toBeInTheDocument();
    expect(screen.getByLabelText(/recipient name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/customer verified via otp/i)).toBeInTheDocument();
  });

  it("switches to the failure branch and shows the reason select", async () => {
    const user = userEvent.setup();
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    await user.click(screen.getByRole("button", { name: /failed/i }));

    expect(screen.getByLabelText(/reason/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/recipient name/i)).not.toBeInTheDocument();
  });

  it("shows the COD field only when the task has a COD amount", () => {
    useMyDeliveriesPanelStore.setState({
      panel: { mode: "record-attempt", task: { ...task, codAmount: 500 } },
    });
    renderWithProviders();

    expect(screen.getByLabelText(/collected cod amount/i)).toBeInTheDocument();
  });

  it("submits a failed attempt with the selected reason and closes on success", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "a1", succeeded: false });
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    await user.click(screen.getByRole("button", { name: /failed/i }));
    await user.click(screen.getByLabelText(/reason/i));
    await user.click(await screen.findByRole("option", { name: "CustomerUnavailable" }));
    await user.click(screen.getByRole("button", { name: /submit/i }));

    await waitFor(() => expect(clientFetchModule.clientFetch).toHaveBeenCalledWith(
      "/api/riders/r1/delivery-attempts/fail",
      expect.objectContaining({ method: "POST" })
    ));
  });

  it("submits a completed delivery with recipient name and OTP flag", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "a1", succeeded: true });
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    await user.type(screen.getByLabelText(/recipient name/i), "Recipient One");
    await user.click(screen.getByLabelText(/customer verified via otp/i));
    await user.click(screen.getByRole("button", { name: /^submit$/i }));

    await waitFor(() => expect(clientFetchModule.clientFetch).toHaveBeenCalledWith(
      "/api/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ method: "POST" })
    ));
  });

  it("requires a collected COD amount before submitting when the task has COD", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "a1", succeeded: true });
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task: { ...task, codAmount: 500 } } });
    renderWithProviders();

    await user.type(screen.getByLabelText(/recipient name/i), "Recipient One");

    expect(screen.getByRole("button", { name: /^submit$/i })).toBeDisabled();
  });
});
