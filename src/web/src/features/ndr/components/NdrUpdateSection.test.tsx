import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NdrUpdateSection } from "./NdrUpdateSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Ndr } from "@/features/ndr/types";

const baseNdr: Ndr = {
  id: "n1", shipmentId: "s1", awb: "AWB001", reason: "CustomerUnavailable",
  assignedStaffUserId: null, status: "Open", customerResponse: null, followUpAt: null,
  nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-09-01T00:00:00Z",
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("NdrUpdateSection", () => {
  it("shows a single forward-move button for Open and submits it", async () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ ...baseNdr, status: "Contacting" });
    const user = userEvent.setup();
    renderWithProviders(<NdrUpdateSection ndr={baseNdr} />);

    expect(screen.queryByRole("button", { name: /move to customer confirmed/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /move to contacting/i }));

    await waitFor(() =>
      expect(spy).toHaveBeenCalledWith("/api/ndrs/n1/update", expect.objectContaining({ method: "POST" }))
    );
  });

  it("shows two branch buttons for CustomerConfirmed", () => {
    renderWithProviders(<NdrUpdateSection ndr={{ ...baseNdr, status: "CustomerConfirmed" }} />);

    expect(screen.getByRole("button", { name: /schedule reattempt/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /request rto/i })).toBeInTheDocument();
  });
});
