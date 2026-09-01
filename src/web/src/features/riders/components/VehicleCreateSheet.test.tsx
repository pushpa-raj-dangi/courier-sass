import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { VehicleCreateSheet } from "./VehicleCreateSheet";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <VehicleCreateSheet />
    </QueryClientProvider>
  );
}

describe("VehicleCreateSheet", () => {
  it("is not visible when the store is closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("New Vehicle")).not.toBeInTheDocument();
  });

  it("is visible with its fields when the store is open", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    useRiderPanelStore.setState({ panel: { mode: "create-vehicle" } });
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("New Vehicle")).toBeInTheDocument());
    expect(screen.getByLabelText(/plate number/i)).toBeInTheDocument();
  });
});
