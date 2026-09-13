import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useRidersSummary } from "./useRidersSummary";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useRidersSummary", () => {
  it("fetches the summary proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ total: 6, active: 5, inactive: 1, currentlyAssigned: 2 }),
    });

    const { result } = renderHook(() => useRidersSummary(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.currentlyAssigned).toBe(2);
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/summary", undefined);
  });
});
