import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RiderCreateSheet } from "./RiderCreateSheet";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RiderCreateSheet />
    </QueryClientProvider>
  );
}

describe("RiderCreateSheet", () => {
  it("is not visible when the store is closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("New Rider")).not.toBeInTheDocument();
  });

  it("is visible with its fields when the store is open", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { id: "b1", code: "KTM", name: "Kathmandu Hub", type: "Hub", isActive: true },
    ]);
    useRiderPanelStore.setState({ panel: { mode: "create-rider" } });
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("New Rider")).toBeInTheDocument());
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/license number/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/base branch/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/role/i)).toBeInTheDocument();
  });
});
