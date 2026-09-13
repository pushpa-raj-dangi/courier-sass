import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useNdrsSummary } from "./useNdrsSummary";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useNdrsSummary", () => {
  it("fetches the summary proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ Open: 4, Resolved: 6 }),
    });

    const { result } = renderHook(() => useNdrsSummary(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.Open).toBe(4);
    expect(global.fetch).toHaveBeenCalledWith("/api/ndrs/summary", undefined);
  });
});
