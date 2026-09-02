import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useOrgDashboard } from "./useOrgDashboard";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useOrgDashboard", () => {
  it("fetches from the BFF proxy route and returns the parsed summary", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        todaysShipmentsByStatus: { Created: 2, Delivered: 5 },
        financial: { expected: 100, collected: 80, pending: 20, settled: 60, merchantPayable: 20 },
        overallDeliverySuccessRate: 0.92,
        overallNdrRate: 0.05,
        overallRtoRate: 0.02,
        averageDeliveryTime: "02:15:30",
      }),
    });

    const { result } = renderHook(() => useOrgDashboard(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.financial.expected).toBe(100);
    expect(global.fetch).toHaveBeenCalledWith("/api/reports/dashboard/organization", undefined);
  });

  it("forwards from/to as a query string when provided", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        todaysShipmentsByStatus: {},
        financial: { expected: 0, collected: 0, pending: 0, settled: 0, merchantPayable: 0 },
        branchPerformance: {},
        riderPerformance: [],
        overallDeliverySuccessRate: null,
        overallNdrRate: null,
        overallRtoRate: null,
        averageDeliveryTime: null,
      }),
    });

    const { result } = renderHook(
      () => useOrgDashboard({ from: "2026-08-01T00:00:00Z", to: "2026-08-31T23:59:59Z" }),
      { wrapper }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith(
      "/api/reports/dashboard/organization?from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z",
      undefined
    );
  });
});
