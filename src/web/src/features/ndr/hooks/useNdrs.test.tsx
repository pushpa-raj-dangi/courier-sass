import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useNdrs } from "./useNdrs";
import { useDeliveryAttempts } from "./useDeliveryAttempts";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

afterEach(() => vi.restoreAllMocks());

describe("NDR hooks", () => {
  it("sends the quick filters to the list route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }) });
    const { result } = renderHook(() => useNdrs({ status: "Open", assigned: "me", reason: "WrongAddress", page: 2, pageSize: 20 }), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/ndrs?status=Open&assigned=me&reason=WrongAddress&page=2&pageSize=20", undefined);
  });

  it("reads delivery attempts by shipment id and stays idle without one", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) });
    const idle = renderHook(() => useDeliveryAttempts(""), { wrapper });
    expect(idle.result.current.fetchStatus).toBe("idle");
    const { result } = renderHook(() => useDeliveryAttempts("s1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/shipments/s1/delivery-attempts", undefined);
  });
});
