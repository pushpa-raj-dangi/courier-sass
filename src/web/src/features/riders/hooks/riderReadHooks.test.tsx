import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useRiderBoard } from "./useRiderBoard";
import { useRider } from "./useRider";
import { useRiderToday } from "./useRiderToday";

let queryClient: QueryClient;
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) });
});

describe("rider read hooks", () => {
  it("useRiderBoard keys by branch and sends branchId only when set", async () => {
    const { result } = renderHook(() => useRiderBoard("b 1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/board?branchId=b%201", undefined);
    expect(queryClient.getQueryCache().find({ queryKey: ["riders", "board", "b 1"] })).toBeDefined();

    const all = renderHook(() => useRiderBoard(), { wrapper });
    await waitFor(() => expect(all.result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/board", undefined);
    expect(queryClient.getQueryCache().find({ queryKey: ["riders", "board", null] })).toBeDefined();
  });

  it("useRider and useRiderToday stay idle without an id and key by rider", async () => {
    renderHook(() => useRider(""), { wrapper });
    renderHook(() => useRiderToday("r1", false), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();

    const rider = renderHook(() => useRider("r1"), { wrapper });
    const today = renderHook(() => useRiderToday("r1", true), { wrapper });
    await waitFor(() => expect(rider.result.current.isSuccess && today.result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/r1", undefined);
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/r1/today", undefined);
    expect(queryClient.getQueryCache().find({ queryKey: ["riders", "r1", "today"] })).toBeDefined();
  });
});
