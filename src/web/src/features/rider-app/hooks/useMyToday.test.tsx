import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useMyToday } from "./useMyToday";

describe("useMyToday", () => {
  it("fetches only when enabled, and refetches when the outbox's task-list prefix is invalidated", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ run: null, tasks: [] }) });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;

    renderHook(() => useMyToday(false), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();

    const { result } = renderHook(() => useMyToday(true), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/me/today", undefined);

    await queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    await waitFor(() => expect(global.fetch).toHaveBeenCalledTimes(2));
  });
});
