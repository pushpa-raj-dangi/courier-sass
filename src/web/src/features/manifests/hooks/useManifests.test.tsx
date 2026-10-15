import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useManifests } from "./useManifests";
import { useManifestShipments } from "./useManifestShipments";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

afterEach(() => vi.restoreAllMocks());

describe("manifest hooks", () => {
  it("sends branch, status and direction to the list route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    const { result } = renderHook(() => useManifests("b1", "Received", { direction: "incoming" }), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/manifests?branchId=b1&status=Received&direction=incoming", undefined);
  });

  it("omits direction when none is given", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    const { result } = renderHook(() => useManifests(undefined, undefined), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/manifests", undefined);
  });

  it("reads a manifest's parcels only when enabled", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    const { result } = renderHook(() => useManifestShipments("m1", true), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/manifests/m1/shipments", undefined);
  });
});
