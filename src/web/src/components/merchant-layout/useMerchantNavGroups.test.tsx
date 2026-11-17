import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { findActiveItem } from "@/components/console/navModel";
import { useMerchantNavGroups } from "./useMerchantNavGroups";

function mockSession(permissions: string[], needsAttention = 0) {
  global.fetch = vi.fn().mockImplementation(async (url: string) => {
    if (url.includes("/api/merchant/shipments")) {
      return {
        ok: true,
        status: 200,
        json: async () => ({ items: [], page: 1, pageSize: 1, totalCount: needsAttention, totalPages: 1 }),
      };
    }
    return {
      ok: true,
      status: 200,
      json: async () => ({
        merchantUserId: "mu1",
        merchantId: "m1",
        organizationId: "o1",
        merchantDisplayName: "Acme Traders",
        email: "owner@acme.test",
        fullName: "Owner Person",
        role: "Owner",
        permissions,
        kycStatus: "Pending",
      }),
    };
  }) as unknown as typeof fetch;
}

const shipmentsBadge = (groups: { items: { label: string; badge?: unknown }[] }[]) =>
  groups.flatMap((g) => g.items).find((i) => i.label === "Shipments")?.badge;

function wrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

const labels = (groups: { label: string; items: { label: string }[] }[]) =>
  groups.map((g) => [g.label, g.items.map((i) => i.label)]);

describe("useMerchantNavGroups", () => {
  it("always shows Dashboard, even with no permissions", async () => {
    mockSession([]);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([["Overview", ["Dashboard"]]]);
  });

  it("hides Staff and API Keys for a Staff-role session", async () => {
    mockSession(["shipment.read", "merchant.pickups.read", "merchant.cod.read", "merchant.kyc.upload"]);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([
      ["Overview", ["Dashboard"]],
      ["Shipping", ["Shipments", "Pickups"]],
      ["Money", ["COD & Invoices"]],
      ["Account", ["KYC"]],
    ]);
  });

  it("shows Staff and API Keys for an Owner-role session", async () => {
    mockSession(["merchant.staff.manage", "merchant.apikeys.manage"]);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([
      ["Overview", ["Dashboard"]],
      ["Account", ["Staff", "API Keys"]],
    ]);
  });

  it("resolves a nested shipments route to the Shipments item", async () => {
    mockSession(["shipment.read"]);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    const active = findActiveItem("/merchant/shipments/abc", result.current.groups);
    expect(active?.item.label).toBe("Shipments");
  });

  it("shows the needs-attention count on Shipments", async () => {
    mockSession(["shipment.read"], 3);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(shipmentsBadge(result.current.groups)).toBe(3));
  });

  it("shows no badge at zero count", async () => {
    mockSession(["shipment.read"], 0);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    await waitFor(() =>
      expect((global.fetch as unknown as { mock: { calls: string[][] } }).mock.calls.some((c) => c[0].includes("/api/merchant/shipments"))).toBe(true)
    );
    await new Promise((r) => setTimeout(r, 50));
    expect(shipmentsBadge(result.current.groups)).toBeUndefined();
  });

  it("shows no badge without shipment.read and never requests the count", async () => {
    mockSession(["merchant.cod.read"], 5);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    await new Promise((r) => setTimeout(r, 50));
    expect(result.current.groups.flatMap((g) => g.items).every((i) => i.badge === undefined)).toBe(true);
  });
});
