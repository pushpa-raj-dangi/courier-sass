import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PlanCreateSheet } from "./PlanCreateSheet";
import { usePlanPanelStore } from "@/features/platform-plans/store/planPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PlanCreateSheet />
    </QueryClientProvider>
  );
}

describe("PlanCreateSheet", () => {
  it("is not visible when the store is closed", () => {
    usePlanPanelStore.setState({ isCreateOpen: false });
    renderWithProviders();
    expect(screen.queryByText("New Plan")).not.toBeInTheDocument();
  });

  it("is visible with its fields when the store is open", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    usePlanPanelStore.setState({ isCreateOpen: true });
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("New Plan")).toBeInTheDocument());
    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^slug$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/monthly price/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/max branches/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/max staff users/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/max monthly shipments/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/make this the default plan/i)).toBeInTheDocument();
  });
});
