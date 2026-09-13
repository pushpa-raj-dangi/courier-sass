import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useShipmentsSummary } from "./useShipmentsSummary";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useShipmentsSummary", () => {
  it("fetches the summary proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ Created: 3, Delivered: 7 }),
    });

    const { result } = renderHook(() => useShipmentsSummary(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.Delivered).toBe(7);
    expect(global.fetch).toHaveBeenCalledWith("/api/shipments/summary", undefined);
  });
});
