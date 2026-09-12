import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformJobs } from "./usePlatformJobs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const overview = {
  summary: { enqueued: 0, scheduled: 0, processing: 0, succeeded: 10, failed: 1, deleted: 0 },
  recurringJobs: [],
  recentJobs: [],
};

describe("usePlatformJobs", () => {
  it("fetches the jobs overview from the proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => overview });

    const { result } = renderHook(() => usePlatformJobs(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/jobs", undefined);
    expect(result.current.data).toEqual(overview);
  });
});
