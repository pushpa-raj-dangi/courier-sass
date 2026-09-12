import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useAuditLogs } from "./useAuditLogs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

describe("useAuditLogs", () => {
  it("builds the query string from page/pageSize with no filters set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => useAuditLogs("", "", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/audit-logs?page=1&pageSize=25", undefined);
  });

  it("includes search and entityType in the query string when set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => useAuditLogs("shipment.created", "Shipment", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/audit-logs?page=1&pageSize=25&search=shipment.created&entityType=Shipment", undefined);
  });

  it("expands date-only dateFrom/dateTo into UTC day-boundary timestamps", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => useAuditLogs("", "", "2026-09-01", "2026-09-05", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith(
      "/api/audit-logs?page=1&pageSize=25&dateFrom=2026-09-01T00%3A00%3A00Z&dateTo=2026-09-05T23%3A59%3A59Z",
      undefined
    );
  });
});
