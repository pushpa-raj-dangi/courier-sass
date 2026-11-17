import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  merchantActiveReturnsCountKey,
  useMerchantActiveReturnsCount,
  useMerchantShipmentCounts,
} from "./useMerchantShipmentCounts";

describe("useMerchantShipmentCounts", () => {
  it("returns null for a failed tab and totals for the rest", async () => {
    global.fetch = vi.fn().mockImplementation(async (url: string) => {
      // needsAttention is the only tab whose query carries DeliveryFailed.
      if (url.includes("status=DeliveryFailed")) return { ok: false, status: 500, json: async () => ({}) };
      return {
        ok: true,
        status: 200,
        json: async () => ({ items: [], page: 1, pageSize: 1, totalCount: 7, totalPages: 7 }),
      };
    }) as unknown as typeof fetch;
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useMerchantShipmentCounts(), { wrapper });

    await waitFor(() => expect(result.current.needsAttention.isLoading).toBe(false));
    await waitFor(() => expect(result.current.all.isLoading).toBe(false));
    expect(result.current.needsAttention.count).toBeNull();
    expect(result.current.all.count).toBe(7);
    expect(result.current.delivered.count).toBe(7);
  });

  it("counts active returns without RtoDelivered under its own key", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 1, totalCount: 4, totalPages: 4 }),
    });
    global.fetch = fetchMock as unknown as typeof fetch;
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useMerchantActiveReturnsCount(), { wrapper });

    await waitFor(() => expect(result.current.count).toBe(4));
    const url = String(fetchMock.mock.calls[0][0]);
    expect(url).toContain("status=RtoInitiated");
    expect(url).toContain("status=RtoInTransit");
    expect(url).toContain("status=RtoAtOrigin");
    expect(url).not.toContain("RtoDelivered");
    expect(client.getQueryData(merchantActiveReturnsCountKey)).toBe(4);
  });
});
