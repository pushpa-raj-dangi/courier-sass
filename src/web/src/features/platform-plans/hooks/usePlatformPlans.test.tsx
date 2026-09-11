import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformPlans, usePlatformPlansForSelect } from "./usePlatformPlans";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

describe("usePlatformPlans", () => {
  it("builds the query string from page/pageSize with no search", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformPlans("", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/plans?page=1&pageSize=25", undefined);
  });

  it("includes search in the query string when set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformPlans("growth", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/plans?page=1&pageSize=25&search=growth", undefined);
  });

  it("reflects the page argument in the query string", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformPlans("", 2, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/plans?page=2&pageSize=25", undefined);
  });
});

describe("usePlatformPlansForSelect", () => {
  it("requests a large page and returns a flat array via data", async () => {
    const plan = { id: "p1", name: "Growth", slug: "growth", monthlyPriceNpr: 3000, maxBranches: null, maxStaffUsers: null, maxMonthlyShipments: null, isDefault: false, isActive: true };
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ ...pagedResult, items: [plan] }) });

    const { result } = renderHook(() => usePlatformPlansForSelect(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/plans?page=1&pageSize=200", undefined);
    expect(result.current.data).toEqual([plan]);
  });
});
