import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMerchantCodSummary } from "./useMerchantCodSummary";
import { useMerchantAwaiting } from "./useMerchantAwaiting";
import { useMerchantShipmentCodStatus } from "./useMerchantShipmentCodStatus";
import { settlementExportUrl } from "../lib/settlementExportUrl";

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

function ok(body: unknown) {
  return { ok: true, status: 200, json: async () => body, text: async () => JSON.stringify(body) };
}

describe("merchant COD hooks", () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue(ok({ x: 1 }));
  });

  it("summary fetches the BFF summary route", async () => {
    const { result } = renderHook(() => useMerchantCodSummary(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(vi.mocked(global.fetch).mock.calls[0][0]).toBe("/api/merchant/cod/summary");
  });

  it("summary makes no request when disabled", () => {
    renderHook(() => useMerchantCodSummary({ enabled: false }), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("awaiting passes page and pageSize", async () => {
    const { result } = renderHook(() => useMerchantAwaiting(2), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(vi.mocked(global.fetch).mock.calls[0][0]).toBe("/api/merchant/cod/awaiting?page=2&pageSize=20");
  });

  it("cod status is disabled without an id", () => {
    renderHook(() => useMerchantShipmentCodStatus(undefined), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("cod status fetches by shipment id", async () => {
    const { result } = renderHook(() => useMerchantShipmentCodStatus("s1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(vi.mocked(global.fetch).mock.calls[0][0]).toBe("/api/merchant/cod/shipments/s1/status");
  });

  it("builds the settlement export url", () => {
    expect(settlementExportUrl("abc")).toBe("/api/merchant/cod/settlements/abc/export");
  });
});
