import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NdrRtoSection } from "./NdrRtoSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Ndr } from "@/features/ndr/types";

const ndr: Ndr = {
  id: "n1", shipmentId: "s1", awb: "AWB001", reason: "CustomerRefused",
  assignedStaffUserId: null, status: "RtoRequested", customerResponse: null, followUpAt: null,
  nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-09-01T00:00:00Z",
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("NdrRtoSection", () => {
  it("disables the resolve button until a reason is entered", async () => {
    renderWithProviders(<NdrRtoSection ndr={ndr} />);
    expect(screen.getByRole("button", { name: /resolve — initiate rto/i })).toBeDisabled();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/rto reason/i), "Customer refused after three contacts");

    expect(screen.getByRole("button", { name: /resolve — initiate rto/i })).toBeEnabled();
  });

  it("submits the resolve request with the entered reason", async () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ ...ndr, status: "Resolved" });
    const user = userEvent.setup();
    renderWithProviders(<NdrRtoSection ndr={ndr} />);

    await user.type(screen.getByLabelText(/rto reason/i), "Customer refused");
    await user.click(screen.getByRole("button", { name: /resolve — initiate rto/i }));

    await waitFor(() =>
      expect(spy).toHaveBeenCalledWith(
        "/api/ndrs/n1/resolve",
        expect.objectContaining({ method: "POST", body: JSON.stringify({ resolution: "Rto", rtoReason: "Customer refused" }) })
      )
    );
  });
});
