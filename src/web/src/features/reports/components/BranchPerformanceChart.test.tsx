import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BranchPerformanceChart } from "./BranchPerformanceChart";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders(data: Parameters<typeof BranchPerformanceChart>[0]["data"]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <BranchPerformanceChart data={data} />
    </QueryClientProvider>
  );
}

describe("BranchPerformanceChart", () => {
  it("sorts branches by shipment volume, descending, and shows their name and rates", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { id: "b1", name: "Kathmandu Hub" },
      { id: "b2", name: "Pokhara Hub" },
    ]);

    renderWithProviders({
      b1: { shipmentVolume: 5, deliveryAttempts: 5, deliverySuccesses: 4, deliverySuccessRate: 0.8, ndrCount: 1, ndrRate: 0.2, rtoCount: 0, rtoRate: 0 },
      b2: { shipmentVolume: 20, deliveryAttempts: 20, deliverySuccesses: 18, deliverySuccessRate: 0.9, ndrCount: 2, ndrRate: 0.1, rtoCount: 0, rtoRate: 0 },
    });

    await waitFor(() => expect(screen.getAllByText("Pokhara Hub").length).toBeGreaterThan(0));
    expect(screen.getAllByText("Kathmandu Hub").length).toBeGreaterThan(0);

    const rowLabels = screen.getAllByText(/Hub$/).map((el) => el.textContent);
    expect(rowLabels[0]).toBe("Pokhara Hub");
  });

  it("shows an empty state when there is no branch performance data", () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    renderWithProviders({});
    expect(screen.getByText("No branch activity yet")).toBeInTheDocument();
  });
});
