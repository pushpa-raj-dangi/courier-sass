import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformAuditLogs } from "./usePlatformAuditLogs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

describe("usePlatformAuditLogs", () => {
  it("builds the query string from page/pageSize with no filters set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformAuditLogs("", "", "", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/audit-logs?page=1&pageSize=25", undefined);
  });

  it("includes organizationSearch and search in the query string when set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformAuditLogs("kathmandu", "shipment.created", "", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/audit-logs?page=1&pageSize=25&organizationSearch=kathmandu&search=shipment.created", undefined);
  });
});
