import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useCodSummary } from "./useCodSummary";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useCodSummary", () => {
  it("fetches the summary proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ expected: 100, collected: 80, pending: 20, settled: 60, merchantPayable: 20 }),
    });

    const { result } = renderHook(() => useCodSummary(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.pending).toBe(20);
    expect(global.fetch).toHaveBeenCalledWith("/api/cod/summary", undefined);
  });
});
