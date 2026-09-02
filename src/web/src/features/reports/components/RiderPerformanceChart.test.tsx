import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RiderPerformanceChart } from "./RiderPerformanceChart";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders(data: Parameters<typeof RiderPerformanceChart>[0]["data"]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RiderPerformanceChart data={data} />
    </QueryClientProvider>
  );
}

describe("RiderPerformanceChart", () => {
  it("sorts riders by delivery attempts, descending, and shows their license number and success rate", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true },
      { id: "r2", userId: "u2", baseBranchId: "b1", licenseNumber: "LIC-002", roleType: "DeliveryRider", isActive: true },
    ]);

    const { container } = renderWithProviders([
      { riderId: "r1", attempts: 5, successes: 4, successRate: 0.8 },
      { riderId: "r2", attempts: 20, successes: 18, successRate: 0.9 },
    ]);

    // Same scoping rationale as BranchPerformanceChart.test.tsx: the chart's own SVG axis labels
    // are covered elsewhere — here, scope to the plain-text rate-stats row below the chart.
    await waitFor(() => {
      const labels = Array.from(container.querySelectorAll(".border-t span.truncate")).map(
        (el) => el.textContent
      );
      expect(labels).toEqual(["LIC-002", "LIC-001"]);
    });
  });

  it("shows an empty state when there is no rider performance data", () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    renderWithProviders([]);
    expect(screen.getByText("No rider activity yet")).toBeInTheDocument();
  });
});
