import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import userEvent from "@testing-library/user-event";
import { AssignTaskSheet } from "./AssignTaskSheet";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AssignTaskSheet />
    </QueryClientProvider>
  );
}

describe("AssignTaskSheet", () => {
  it("is not visible when the store is closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("Assign Task")).not.toBeInTheDocument();
  });

  it("looks up a shipment by AWB and shows an eligible rider picker", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: string) => {
      if (path.startsWith("/api/shipments/")) {
        return {
          id: "s1", awb: "AWB001", currentStatus: "Created",
          originBranchId: "b1", destinationBranchId: "b2",
          recipientName: "Recipient One", codAmount: 0,
        } as never;
      }
      if (path === "/api/riders") {
        return [{ id: "r1", baseBranchId: "b1", licenseNumber: "LIC-1", roleType: "PickupRider", isActive: true }] as never;
      }
      return [] as never;
    });
    useRiderPanelStore.setState({ panel: { mode: "assign-task" } });
    renderWithProviders();

    await user.type(screen.getByLabelText(/awb/i), "AWB001{enter}");

    await waitFor(() => expect(screen.getByText(/Recipient One/)).toBeInTheDocument());
    expect(screen.getByLabelText(/rider/i)).toBeInTheDocument();
  });
});
