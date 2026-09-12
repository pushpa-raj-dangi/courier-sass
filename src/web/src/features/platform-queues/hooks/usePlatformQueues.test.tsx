import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformQueues } from "./usePlatformQueues";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const queues = [{ name: "courier.notifications", messages: 0, messagesReady: 0, messagesUnacknowledged: 0, consumers: 1, state: "running" }];

describe("usePlatformQueues", () => {
  it("fetches the queue list from the proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => queues });

    const { result } = renderHook(() => usePlatformQueues(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/queues", undefined);
    expect(result.current.data).toEqual(queues);
  });
});
