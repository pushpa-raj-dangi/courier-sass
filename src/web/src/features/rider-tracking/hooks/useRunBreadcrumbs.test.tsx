import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useRunBreadcrumbs } from "./useRunBreadcrumbs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useRunBreadcrumbs", () => {
  it("fetches only with a run id and when enabled", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    renderHook(() => useRunBreadcrumbs(null, true), { wrapper });
    renderHook(() => useRunBreadcrumbs("run1", false), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();

    const { result } = renderHook(() => useRunBreadcrumbs("run1", true), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/runs/run1/breadcrumbs", undefined);
  });
});
